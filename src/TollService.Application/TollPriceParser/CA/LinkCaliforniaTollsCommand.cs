using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.CA;

public record CaliforniaRate(
    [property: System.Text.Json.Serialization.JsonPropertyName("day_period")] string? DayPeriod,
    [property: System.Text.Json.Serialization.JsonPropertyName("direction")] string? Direction,
    [property: System.Text.Json.Serialization.JsonPropertyName("time_window")] string? TimeWindow,
    [property: System.Text.Json.Serialization.JsonPropertyName("account_toll")] double AccountToll,
    [property: System.Text.Json.Serialization.JsonPropertyName("non_account_toll")] double NonAccountToll);

public record CaliforniaTollPoint(
    [property: System.Text.Json.Serialization.JsonPropertyName("toll_point_name")] string TollPointName,
    [property: System.Text.Json.Serialization.JsonPropertyName("rate_type")] string? RateType,
    [property: System.Text.Json.Serialization.JsonPropertyName("rates")] List<CaliforniaRate>? Rates);

public record CaliforniaPricesData(
    [property: System.Text.Json.Serialization.JsonPropertyName("road_name")] string? RoadName,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_class_id")] int? VehicleClassId,
    [property: System.Text.Json.Serialization.JsonPropertyName("description")] string? Description,
    [property: System.Text.Json.Serialization.JsonPropertyName("toll_points")] List<CaliforniaTollPoint>? TollPoints);

public record CaliforniaPaymentMethods(
    [property: System.Text.Json.Serialization.JsonPropertyName("tag")] bool Tag,
    [property: System.Text.Json.Serialization.JsonPropertyName("plate")] bool Plate,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] bool Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("card")] bool Card,
    [property: System.Text.Json.Serialization.JsonPropertyName("app")] bool App);

public record LinkCaliforniaTollsCommand(string JsonPayload) : IRequest<LinkCaliforniaTollsResult>;

public record CaliforniaFoundTollInfo(
    string TollPointName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber);

public record LinkCaliforniaTollsResult(
    List<CaliforniaFoundTollInfo> FoundTolls,
    List<string> NotFoundTollPoints,
    int UpdatedTollsCount,
    string? Error = null);

public class LinkCaliforniaTollsCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkCaliforniaTollsCommand, LinkCaliforniaTollsResult>
{
    // California bounds: approximate (south, west, north, east)
    // South: ~32.5°N, West: ~124.5°W, North: ~42.0°N, East: ~114.0°W
    private static readonly double CaMinLatitude = 32.5;
    private static readonly double CaMinLongitude = -124.5;
    private static readonly double CaMaxLatitude = 42.0;
    private static readonly double CaMaxLongitude = -114.0;

    public async Task<LinkCaliforniaTollsResult> Handle(LinkCaliforniaTollsCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkCaliforniaTollsResult(new(), new(), 0, "JSON payload is empty");
        }

        CaliforniaPricesData? data = null;
        string? link = null;
        PaymentMethod? paymentMethod = null;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            await Task.Yield();

            // Используем JsonDocument для обработки полей link и payment_methods
            using (JsonDocument doc = JsonDocument.Parse(request.JsonPayload))
            {
                // Десериализуем основные данные
                data = JsonSerializer.Deserialize<CaliforniaPricesData>(request.JsonPayload, options);

                // Читаем link
                if (doc.RootElement.TryGetProperty("link", out var linkElement) && linkElement.ValueKind == JsonValueKind.String)
                {
                    link = linkElement.GetString();
                }

                // Читаем payment_methods
                if (doc.RootElement.TryGetProperty("payment_methods", out var paymentMethodsElement))
                {
                    var paymentMethods = JsonSerializer.Deserialize<CaliforniaPaymentMethods>(paymentMethodsElement.GetRawText(), options);
                    if (paymentMethods != null)
                    {
                        // Маппинг: plate -> NoPlate (обратная логика), card -> NoCard (обратная логика)
                        paymentMethod = new PaymentMethod(
                            tag: paymentMethods.Tag,
                            noPlate: !paymentMethods.Plate,
                            cash: paymentMethods.Cash,
                            noCard: !paymentMethods.Card,
                            app: paymentMethods.App);
                    }
                }
            }
        }
        catch (JsonException jsonEx)
        {
            return new LinkCaliforniaTollsResult(new(), new(), 0, $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.TollPoints == null || data.TollPoints.Count == 0)
        {
            if (data == null)
            {
                return new LinkCaliforniaTollsResult(new(), new(), 0, "Не удалось распарсить JSON. Проверьте структуру данных.");
            }
            return new LinkCaliforniaTollsResult(new(), new(), 0, "Toll points не найдены в JSON (массив 'toll_points' пуст или отсутствует).");
        }

        // Определяем AxelType из vehicle_class_id
        var axelType = data.VehicleClassId switch
        {
            1 => AxelType._1L,
            2 => AxelType._2L,
            3 => AxelType._3L,
            4 => AxelType._4L,
            5 => AxelType._5L,
            6 => AxelType._6L,
            7 => AxelType._7L,
            8 => AxelType._8L,
            9 => AxelType._9L,
            _ => AxelType._5L // По умолчанию, если не указан
        };

        // Создаем bounding box для California
        var caBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            CaMinLongitude,
            CaMinLatitude,
            CaMaxLongitude,
            CaMaxLatitude);

        // Собираем все уникальные имена toll points для оптимизированного поиска
        var allTollPointNames = data.TollPoints
            .Where(tp => !string.IsNullOrWhiteSpace(tp.TollPointName))
            .Select(tp => tp.TollPointName)
            .Distinct()
            .ToList();

        if (allTollPointNames.Count == 0)
        {
            return new LinkCaliforniaTollsResult(
                new(),
                new(),
                0,
                "Не найдено ни одного имени toll point");
        }

        // Оптимизированный поиск tolls: один запрос к БД
        var tollsByTollPointName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allTollPointNames,
            caBoundingBox,
            TollSearchOptions.NameOrKey,
            websiteUrl: link,
            paymentMethod: paymentMethod,
            ct);

        var foundTolls = new List<CaliforniaFoundTollInfo>();
        var notFoundTollPoints = new List<string>();
        var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

        foreach (var tollPoint in data.TollPoints)
        {
            if (string.IsNullOrWhiteSpace(tollPoint.TollPointName))
            {
                notFoundTollPoints.Add("Toll point with empty name");
                continue;
            }

            // Получаем найденные tolls из словаря
            if (!tollsByTollPointName.TryGetValue(tollPoint.TollPointName, out var tolls) || tolls.Count == 0)
            {
                notFoundTollPoints.Add(tollPoint.TollPointName);
                continue;
            }

            // Добавляем все найденные tolls и обрабатываем цены
            foreach (var toll in tolls)
            {
                foundTolls.Add(new CaliforniaFoundTollInfo(
                    TollPointName: tollPoint.TollPointName,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    TollKey: toll.Key,
                    TollNumber: toll.Number));

                // Обрабатываем rates для этого toll
                if (tollPoint.Rates != null && tollPoint.Rates.Count > 0)
                {
                    foreach (var rate in tollPoint.Rates)
                    {
                        var (dayOfWeekFrom, dayOfWeekTo) = ParseDayPeriod(rate.DayPeriod);
                        var (timeFrom, timeTo) = ParseTimeWindow(rate.TimeWindow);
                        var timeOfDay = DetermineTimeOfDay(timeFrom, timeTo);

                        // Обрабатываем account_toll (будет автоматически преобразован в AccountToll для CA через маппинг в сервисе)
                        if (rate.AccountToll > 0)
                        {
                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: rate.AccountToll,
                                PaymentType: TollPaymentType.AccountToll,
                                AxelType: axelType,
                                DayOfWeekFrom: dayOfWeekFrom,
                                DayOfWeekTo: dayOfWeekTo,
                                TimeOfDay: timeOfDay,
                                TimeFrom: timeFrom,
                                TimeTo: timeTo,
                                Description: $"{tollPoint.TollPointName} - {rate.Direction}"));
                        }

                        // Обрабатываем non_account_toll (будет автоматически преобразован в NonAccountToll для CA через маппинг в сервисе)
                        if (rate.NonAccountToll > 0)
                        {
                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: rate.NonAccountToll,
                                PaymentType: TollPaymentType.NonAccountToll,
                                AxelType: axelType,
                                DayOfWeekFrom: dayOfWeekFrom,
                                DayOfWeekTo: dayOfWeekTo,
                                TimeOfDay: timeOfDay,
                                TimeFrom: timeFrom,
                                TimeTo: timeTo,
                                Description: $"{tollPoint.TollPointName} - {rate.Direction}"));
                        }
                    }
                }
            }
        }

        // Пакетная установка цен
        int updatedTollsCount = 0;
        if (tollsToUpdatePrices.Count > 0)
        {
            // Конвертируем List в IEnumerable для метода
            var tollsToUpdatePricesEnumerable = tollsToUpdatePrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPriceData>)kvp.Value);

            // ЗАКОММЕНТИРОВАНО: Маппинг типов оплаты для Калифорнии
            // Раскомментируйте, если нужно обновлять существующие цены со старых типов (EZPass/Cash) на новые (AccountToll/NonAccountToll)
            /*
            var paymentTypeMapping = new Dictionary<TollPaymentType, TollPaymentType>
            {
                { TollPaymentType.EZPass, TollPaymentType.AccountToll },
                { TollPaymentType.Cash, TollPaymentType.NonAccountToll }
            };
            */

            var updatedPricesResult = await _calculatePriceService.SetTollPricesDirectlyBatchAsync(
                tollsToUpdatePricesEnumerable,
                paymentTypeMapping: null, // Маппинг закомментирован - используем типы напрямую
                ct);
            updatedTollsCount = updatedPricesResult.Count;
        }

        // Сохраняем все изменения
        await _context.SaveChangesAsync(ct);

        return new LinkCaliforniaTollsResult(
            foundTolls,
            notFoundTollPoints,
            updatedTollsCount);
    }

    private static TollPriceTimeOfDay DetermineTimeOfDay(TimeOnly timeFrom, TimeOnly timeTo)
    {
        // Если время не задано, возвращаем Any
        if (timeFrom == default && timeTo == default)
            return TollPriceTimeOfDay.Any;

        // Определяем, день это или ночь на основе времени
        // Обычно день: 6:00 - 21:00, ночь: 21:00 - 6:00
        // Но здесь используем более простую логику: если время начинается после 6:00 и до 21:00, это день
        if (timeFrom != default)
        {
            var hour = timeFrom.Hour;
            if (hour >= 6 && hour < 21)
                return TollPriceTimeOfDay.Day;
            return TollPriceTimeOfDay.Night;
        }

        return TollPriceTimeOfDay.Any;
    }

    private static (TollPriceDayOfWeek from, TollPriceDayOfWeek to) ParseDayPeriod(string? dayPeriod)
    {
        if (string.IsNullOrWhiteSpace(dayPeriod))
            return (TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any);

        var periodLower = dayPeriod.ToLower();

        if (periodLower.Contains("monday through friday") || periodLower.Contains("monday-friday"))
            return (TollPriceDayOfWeek.Monday, TollPriceDayOfWeek.Friday);

        if (periodLower.Contains("saturday and sunday") || periodLower.Contains("saturday-sunday") || periodLower.Contains("weekend"))
            return (TollPriceDayOfWeek.Saturday, TollPriceDayOfWeek.Sunday);

        if (periodLower.Contains("saturday"))
            return (TollPriceDayOfWeek.Saturday, TollPriceDayOfWeek.Saturday);

        if (periodLower.Contains("sunday"))
            return (TollPriceDayOfWeek.Sunday, TollPriceDayOfWeek.Sunday);

        return (TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any);
    }

    private static (TimeOnly from, TimeOnly to) ParseTimeWindow(string? timeWindow)
    {
        if (string.IsNullOrWhiteSpace(timeWindow))
            return (default, default);

        // Формат: "12:00 a.m. - 6:59 a.m." или "7:00 a.m. - 7:29 a.m."
        var pattern = @"(\d{1,2}):(\d{2})\s*(a\.m\.|p\.m\.)\s*-\s*(\d{1,2}):(\d{2})\s*(a\.m\.|p\.m\.)";
        var match = Regex.Match(timeWindow, pattern, RegexOptions.IgnoreCase);

        if (match.Success)
        {
            try
            {
                var fromHour = int.Parse(match.Groups[1].Value);
                var fromMinute = int.Parse(match.Groups[2].Value);
                var fromPeriod = match.Groups[3].Value.ToLower();
                var toHour = int.Parse(match.Groups[4].Value);
                var toMinute = int.Parse(match.Groups[5].Value);
                var toPeriod = match.Groups[6].Value.ToLower();

                // Конвертируем в 24-часовой формат
                if (fromPeriod.Contains("p.m.") && fromHour != 12)
                    fromHour += 12;
                if (fromPeriod.Contains("a.m.") && fromHour == 12)
                    fromHour = 0;

                if (toPeriod.Contains("p.m.") && toHour != 12)
                    toHour += 12;
                if (toPeriod.Contains("a.m.") && toHour == 12)
                    toHour = 0;

                var timeFrom = new TimeOnly(fromHour, fromMinute);
                var timeTo = new TimeOnly(toHour, toMinute);

                return (timeFrom, timeTo);
            }
            catch
            {
                // Если не удалось распарсить, возвращаем default
            }
        }

        return (default, default);
    }
}

