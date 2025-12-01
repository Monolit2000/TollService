using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
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
    int UpdatedCount,
    int CreatedCount,
    string? Error = null);

public class LinkCaliforniaTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<LinkCaliforniaTollsCommand, LinkCaliforniaTollsResult>
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
            return new LinkCaliforniaTollsResult(new(), new(), 0, 0, "JSON payload is empty");
        }

        CaliforniaPricesData? data = null;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            await Task.Yield();
            data = JsonSerializer.Deserialize<CaliforniaPricesData>(request.JsonPayload, options);
        }
        catch (JsonException jsonEx)
        {
            return new LinkCaliforniaTollsResult(new(), new(), 0, 0, $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.TollPoints == null || data.TollPoints.Count == 0)
        {
            if (data == null)
            {
                return new LinkCaliforniaTollsResult(new(), new(), 0, 0, "Не удалось распарсить JSON. Проверьте структуру данных.");
            }
            return new LinkCaliforniaTollsResult(new(), new(), 0, 0, "Toll points не найдены в JSON (массив 'toll_points' пуст или отсутствует).");
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
        var caBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(CaMinLongitude, CaMinLatitude),
            new Coordinate(CaMaxLongitude, CaMinLatitude),
            new Coordinate(CaMaxLongitude, CaMaxLatitude),
            new Coordinate(CaMinLongitude, CaMaxLatitude),
            new Coordinate(CaMinLongitude, CaMinLatitude)
        }))
        { SRID = 4326 };

        var foundTolls = new List<CaliforniaFoundTollInfo>();
        var notFoundTollPoints = new List<string>();
        int updatedCount = 0;
        int createdCount = 0;

        foreach (var tollPoint in data.TollPoints)
        {
            if (string.IsNullOrWhiteSpace(tollPoint.TollPointName))
            {
                notFoundTollPoints.Add("Toll point with empty name");
                continue;
            }

            var tollPointNameLower = tollPoint.TollPointName.ToLower();

            // Ищем tolls по name или key в пределах California
            // Сначала ищем точное совпадение (без учета регистра)
            var tolls = await _context.Tolls
                .Where(t =>
                    t.Location != null &&
                    caBoundingBox.Contains(t.Location) &&
                    ((t.Name != null && t.Name.ToLower() == tollPointNameLower) ||
                     (t.Key != null && t.Key.ToLower() == tollPointNameLower)))
                .ToListAsync(ct);

            // Если не нашли точное совпадение, пробуем частичное совпадение
            if (tolls.Count == 0)
            {
                // Загружаем все tolls в пределах bounding box и фильтруем в памяти
                var allTollsInBox = await _context.Tolls
                    .Where(t =>
                        t.Location != null &&
                        caBoundingBox.Contains(t.Location))
                    .ToListAsync(ct);

                tolls = allTollsInBox
                    .Where(t =>
                        (t.Name != null && (t.Name.ToLower().Contains(tollPointNameLower) || tollPointNameLower.Contains(t.Name.ToLower()))) ||
                        (t.Key != null && (t.Key.ToLower().Contains(tollPointNameLower) || tollPointNameLower.Contains(t.Key.ToLower()))))
                    .ToList();
            }

            // Фильтруем tolls: исключаем те, у которых имя и ключ пустые или невалидные
            tolls = tolls
                .Where(t => IsValidTollNameOrKey(t.Name) || IsValidTollNameOrKey(t.Key))
                .ToList();

            if (tolls.Count == 0)
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
                        // Обрабатываем account_toll (EZPass)
                        if (rate.AccountToll > 0)
                        {
                            var (dayOfWeekFrom, dayOfWeekTo) = ParseDayPeriod(rate.DayPeriod);
                            var (timeFrom, timeTo) = ParseTimeWindow(rate.TimeWindow);

                            var existingEzPass = await _context.TollPrices
                                .FirstOrDefaultAsync(tp =>
                                    tp.TollId == toll.Id &&
                                    tp.PaymentType == TollPaymentType.EZPass &&
                                    tp.AxelType == axelType &&
                                    tp.DayOfWeekFrom == dayOfWeekFrom &&
                                    tp.DayOfWeekTo == dayOfWeekTo &&
                                    tp.TimeFrom == timeFrom &&
                                    tp.TimeTo == timeTo,
                                    ct);

                            if (existingEzPass != null)
                            {
                                existingEzPass.Amount = rate.AccountToll;
                                updatedCount++;
                            }
                            else
                            {
                                var newEzPassPrice = new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = toll.Id,
                                    PaymentType = TollPaymentType.EZPass,
                                    AxelType = axelType,
                                    DayOfWeekFrom = dayOfWeekFrom,
                                    DayOfWeekTo = dayOfWeekTo,
                                    TimeFrom = timeFrom,
                                    TimeTo = timeTo,
                                    Amount = rate.AccountToll,
                                    Description = $"{tollPoint.TollPointName} - {rate.Direction}"
                                };
                                toll.TollPrices.Add(newEzPassPrice);
                                _context.TollPrices.Add(newEzPassPrice);
                                createdCount++;
                            }
                        }

                        // Обрабатываем non_account_toll (Cash)
                        if (rate.NonAccountToll > 0)
                        {
                            var (dayOfWeekFrom, dayOfWeekTo) = ParseDayPeriod(rate.DayPeriod);
                            var (timeFrom, timeTo) = ParseTimeWindow(rate.TimeWindow);

                            var existingCash = await _context.TollPrices
                                .FirstOrDefaultAsync(tp =>
                                    tp.TollId == toll.Id &&
                                    tp.PaymentType == TollPaymentType.Cash &&
                                    tp.AxelType == axelType &&
                                    tp.DayOfWeekFrom == dayOfWeekFrom &&
                                    tp.DayOfWeekTo == dayOfWeekTo &&
                                    tp.TimeFrom == timeFrom &&
                                    tp.TimeTo == timeTo,
                                    ct);

                            if (existingCash != null)
                            {
                                existingCash.Amount = rate.NonAccountToll;
                                updatedCount++;
                            }
                            else
                            {
                                var newCashPrice = new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = toll.Id,
                                    PaymentType = TollPaymentType.Cash,
                                    AxelType = axelType,
                                    DayOfWeekFrom = dayOfWeekFrom,
                                    DayOfWeekTo = dayOfWeekTo,
                                    TimeFrom = timeFrom,
                                    TimeTo = timeTo,
                                    Amount = rate.NonAccountToll,
                                    Description = $"{tollPoint.TollPointName} - {rate.Direction}"
                                };
                                toll.TollPrices.Add(newCashPrice);
                                _context.TollPrices.Add(newCashPrice);
                                createdCount++;
                            }
                        }
                    }
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        return new LinkCaliforniaTollsResult(
            foundTolls,
            notFoundTollPoints,
            updatedCount,
            createdCount);
    }

    private static bool IsValidTollNameOrKey(string? nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey))
            return false;

        // Убираем пробелы и проверяем, не является ли строка только подчеркиваниями
        var trimmed = nameOrKey.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "_" || trimmed.All(c => c == '_'))
            return false;

        return true;
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

