using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.CO;

// Формат E470: license_plate_all_hours, expresstoll_12pm_to_9am, expresstoll_9am_to_12pm
public record ColoradoPlazaRatesE470(
    [property: JsonPropertyName("license_plate_all_hours")] double? LicensePlateAllHours,
    [property: JsonPropertyName("expresstoll_12pm_to_9am")] double? ExpressToll12pmTo9am,
    [property: JsonPropertyName("expresstoll_9am_to_12pm")] double? ExpressToll9amTo12pm);

// Формат Northway: rate_6am_to_9pm, rate_9pm_to_6am
public record ColoradoPlazaRatesNorthway(
    [property: JsonPropertyName("rate_6am_to_9pm")] double? Rate6amTo9pm,
    [property: JsonPropertyName("rate_9pm_to_6am")] double? Rate9pmTo6am);

public record ColoradoAxelRatesE470(
    [property: JsonPropertyName("5_axles")] ColoradoPlazaRatesE470? Axles5,
    [property: JsonPropertyName("6_axles")] ColoradoPlazaRatesE470? Axles6);

public record ColoradoAxelRatesNorthway(
    [property: JsonPropertyName("5_axles")] ColoradoPlazaRatesNorthway? Axles5,
    [property: JsonPropertyName("6_axles")] ColoradoPlazaRatesNorthway? Axles6);

// Универсальная структура для обоих форматов
public record ColoradoPlaza(
    [property: JsonPropertyName("plaza_name")] string PlazaName,
    [property: JsonPropertyName("rates")] JsonElement? Rates);

public record LinkColoradoTollsCommand(string JsonPayload) : IRequest<LinkColoradoTollsResult>;

public record ColoradoFoundTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber);

public record LinkColoradoTollsResult(
    List<ColoradoFoundTollInfo> FoundTolls,
    List<string> NotFoundPlazas,
    string? Error = null);

// Colorado bounds: (south, west, north, east) = (36.9, -109.0, 41.0, -102.0)
public class LinkColoradoTollsCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkColoradoTollsCommand, LinkColoradoTollsResult>
{
    private static readonly double CoMinLatitude = 36.9;
    private static readonly double CoMinLongitude = -109.0;
    private static readonly double CoMaxLatitude = 41.0;
    private static readonly double CoMaxLongitude = -102.0;

    public async Task<LinkColoradoTollsResult> Handle(LinkColoradoTollsCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkColoradoTollsResult(new(), new(), "JSON payload is empty");
        }

        List<ColoradoPlaza>? plazas;
        try
        {
            plazas = JsonSerializer.Deserialize<List<ColoradoPlaza>>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new LinkColoradoTollsResult(new(), new(), $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (plazas == null || plazas.Count == 0)
        {
            return new LinkColoradoTollsResult(new(), new(), "Plazas не найдены в JSON (массив пуст или отсутствует).");
        }

        // Создаем bounding box для Colorado
        var coBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            CoMinLongitude,
            CoMinLatitude,
            CoMaxLongitude,
            CoMaxLatitude);

        // Собираем все уникальные имена плаз для оптимизированного поиска
        var allPlazaNames = plazas
            .Where(p => !string.IsNullOrWhiteSpace(p.PlazaName))
            .Select(p => p.PlazaName)
            .Distinct()
            .ToList();

        if (allPlazaNames.Count == 0)
        {
            return new LinkColoradoTollsResult(
                new(),
                new(),
                "Не найдено ни одного имени плазы");
        }

        // Оптимизированный поиск tolls: один запрос к БД
        var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allPlazaNames,
            coBoundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        var foundTolls = new List<ColoradoFoundTollInfo>();
        var notFoundPlazas = new List<string>();
        var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

        foreach (var plaza in plazas)
        {
            if (string.IsNullOrWhiteSpace(plaza.PlazaName))
            {
                notFoundPlazas.Add("Plaza with empty name");
                continue;
            }

            // Ищем tolls по имени плазы
            if (!tollsByPlazaName.TryGetValue(plaza.PlazaName.ToLower(), out var foundTollsForPlaza) || foundTollsForPlaza.Count == 0)
            {
                notFoundPlazas.Add(plaza.PlazaName);
                continue;
            }

            // Обрабатываем цены для каждой найденной плазы
            foreach (var toll in foundTollsForPlaza)
            {
                // Собираем цены из JSON
                CollectPricesFromJson(toll, plaza.Rates, tollsToUpdatePrices, plaza.PlazaName);

                foundTolls.Add(new ColoradoFoundTollInfo(
                    PlazaName: plaza.PlazaName,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    TollKey: toll.Key,
                    TollNumber: toll.Number));
            }
        }

        // Батч-установка цен
        int updatedTollsCount = 0;
        if (tollsToUpdatePrices.Count > 0)
        {
            // Конвертируем List в IEnumerable для метода
            var tollsToUpdatePricesEnumerable = tollsToUpdatePrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPriceData>)kvp.Value);

            var updatedPricesResult = await _calculatePriceService.SetTollPricesDirectlyBatchAsync(
                tollsToUpdatePricesEnumerable,
                ct);
            updatedTollsCount = updatedPricesResult.Count;
        }

        // Сохраняем все изменения
        await _context.SaveChangesAsync(ct);

        return new LinkColoradoTollsResult(
            foundTolls,
            notFoundPlazas.Distinct().ToList());
    }

    private void CollectPricesFromJson(Toll toll, JsonElement? ratesElement, Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices, string plazaName)
    {
        if (!ratesElement.HasValue || ratesElement.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var rates = ratesElement.Value;

        // Определяем формат JSON: проверяем наличие полей для E470 или Northway
        bool isE470Format = rates.TryGetProperty("5_axles", out var axles5E470) &&
                           axles5E470.TryGetProperty("license_plate_all_hours", out _);

        bool isNorthwayFormat = rates.TryGetProperty("5_axles", out var axles5Northway) &&
                               axles5Northway.TryGetProperty("rate_6am_to_9pm", out _);

        if (isE470Format)
        {
            CollectPricesFromE470Format(toll, rates, tollsToUpdatePrices, plazaName);
        }
        else if (isNorthwayFormat)
        {
            CollectPricesFromNorthwayFormat(toll, rates, tollsToUpdatePrices, plazaName);
        }
    }

    private void CollectPricesFromE470Format(Toll toll, JsonElement rates, Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices, string plazaName)
    {
        // Обрабатываем 5_axles и 6_axles
        foreach (var axlesKey in new[] { "5_axles", "6_axles" })
        {
            if (!rates.TryGetProperty(axlesKey, out var axlesRates))
            {
                continue;
            }

            var axelType = axlesKey == "5_axles" ? AxelType._5L : AxelType._6L;

            // license_plate_all_hours -> PayOnline, Any time
            if (axlesRates.TryGetProperty("license_plate_all_hours", out var licensePlateAllHours) &&
                licensePlateAllHours.ValueKind == JsonValueKind.Number)
            {
                var amount = licensePlateAllHours.GetDouble();
                if (amount > 0)
                {
                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.PayOnline,
                        AxelType: axelType,
                        TimeOfDay: TollPriceTimeOfDay.Any,
                        Description: $"Colorado E470 {plazaName} - License Plate All Hours ({axlesKey})"));
                }
            }

            // expresstoll_12pm_to_9am -> IPass, Night (12pm-9am это ночь, 12:00 до 9:00 следующего дня)
            if (axlesRates.TryGetProperty("expresstoll_12pm_to_9am", out var expressToll12pmTo9am) &&
                expressToll12pmTo9am.ValueKind == JsonValueKind.Number)
            {
                var amount = expressToll12pmTo9am.GetDouble();
                if (amount > 0)
                {
                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.IPass,
                        AxelType: axelType,
                        TimeOfDay: TollPriceTimeOfDay.Night,
                        TimeFrom: new TimeOnly(12, 0), // 12:00 PM
                        TimeTo: new TimeOnly(9, 0),    // 9:00 AM следующего дня
                        Description: $"Colorado E470 {plazaName} - ExpressToll 12pm-9am ({axlesKey})"));
                }
            }

            // expresstoll_9am_to_12pm -> IPass, Day (9am-12pm это день, 9:00 до 12:00)
            if (axlesRates.TryGetProperty("expresstoll_9am_to_12pm", out var expressToll9amTo12pm) &&
                expressToll9amTo12pm.ValueKind == JsonValueKind.Number)
            {
                var amount = expressToll9amTo12pm.GetDouble();
                if (amount > 0)
                {
                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.IPass,
                        AxelType: axelType,
                        TimeOfDay: TollPriceTimeOfDay.Day,
                        TimeFrom: new TimeOnly(9, 0),  // 9:00 AM
                        TimeTo: new TimeOnly(12, 0),   // 12:00 PM
                        Description: $"Colorado E470 {plazaName} - ExpressToll 9am-12pm ({axlesKey})"));
                }
            }
        }
    }

    private void CollectPricesFromNorthwayFormat(Toll toll, JsonElement rates, Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices, string plazaName)
    {
        // Обрабатываем 5_axles и 6_axles
        foreach (var axlesKey in new[] { "5_axles", "6_axles" })
        {
            if (!rates.TryGetProperty(axlesKey, out var axlesRates))
            {
                continue;
            }

            var axelType = axlesKey == "5_axles" ? AxelType._5L : AxelType._6L;

            // rate_6am_to_9pm -> PayOnline, Day (6:00 до 21:00)
            if (axlesRates.TryGetProperty("rate_6am_to_9pm", out var rate6amTo9pm) &&
                rate6amTo9pm.ValueKind == JsonValueKind.Number)
            {
                var amount = rate6amTo9pm.GetDouble();
                if (amount > 0)
                {
                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.PayOnline,
                        AxelType: axelType,
                        TimeOfDay: TollPriceTimeOfDay.Day,
                        TimeFrom: new TimeOnly(6, 0),   // 6:00 AM
                        TimeTo: new TimeOnly(21, 0),    // 9:00 PM (21:00)
                        Description: $"Colorado Northway {plazaName} - Rate 6am-9pm ({axlesKey})"));
                }
            }

            // rate_9pm_to_6am -> PayOnline, Night (21:00 до 6:00 следующего дня)
            if (axlesRates.TryGetProperty("rate_9pm_to_6am", out var rate9pmTo6am) &&
                rate9pmTo6am.ValueKind == JsonValueKind.Number)
            {
                var amount = rate9pmTo6am.GetDouble();
                if (amount > 0)
                {
                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.PayOnline,
                        AxelType: axelType,
                        TimeOfDay: TollPriceTimeOfDay.Night,
                        TimeFrom: new TimeOnly(21, 0),  // 9:00 PM (21:00)
                        TimeTo: new TimeOnly(6, 0),     // 6:00 AM следующего дня
                        Description: $"Colorado Northway {plazaName} - Rate 9pm-6am ({axlesKey})"));
                }
            }
        }
    }
}

