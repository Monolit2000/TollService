using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.CO;

// Формат E470: license_plate_all_hours, expresstoll_12pm_to_9am, expresstoll_9am_to_12pm
public record ColoradoPlazaRatesE470(
    [property: System.Text.Json.Serialization.JsonPropertyName("license_plate_all_hours")] double? LicensePlateAllHours,
    [property: System.Text.Json.Serialization.JsonPropertyName("expresstoll_12pm_to_9am")] double? ExpressToll12pmTo9am,
    [property: System.Text.Json.Serialization.JsonPropertyName("expresstoll_9am_to_12pm")] double? ExpressToll9amTo12pm);

// Формат Northway: rate_6am_to_9pm, rate_9pm_to_6am
public record ColoradoPlazaRatesNorthway(
    [property: System.Text.Json.Serialization.JsonPropertyName("rate_6am_to_9pm")] double? Rate6amTo9pm,
    [property: System.Text.Json.Serialization.JsonPropertyName("rate_9pm_to_6am")] double? Rate9pmTo6am);

public record ColoradoAxelRatesE470(
    [property: System.Text.Json.Serialization.JsonPropertyName("5_axles")] ColoradoPlazaRatesE470? Axles5,
    [property: System.Text.Json.Serialization.JsonPropertyName("6_axles")] ColoradoPlazaRatesE470? Axles6);

public record ColoradoAxelRatesNorthway(
    [property: System.Text.Json.Serialization.JsonPropertyName("5_axles")] ColoradoPlazaRatesNorthway? Axles5,
    [property: System.Text.Json.Serialization.JsonPropertyName("6_axles")] ColoradoPlazaRatesNorthway? Axles6);

// Универсальная структура для обоих форматов
public record ColoradoPlaza(
    [property: System.Text.Json.Serialization.JsonPropertyName("plaza_name")] string PlazaName,
    [property: System.Text.Json.Serialization.JsonPropertyName("rates")] JsonElement? Rates);

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

public class LinkColoradoTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<LinkColoradoTollsCommand, LinkColoradoTollsResult>
{
    // Colorado bounds: (south, west, north, east) = (36.9, -109.0, 41.0, -102.0)
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
            await Task.Yield();
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
        var coBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(CoMinLongitude, CoMinLatitude),
            new Coordinate(CoMaxLongitude, CoMinLatitude),
            new Coordinate(CoMaxLongitude, CoMaxLatitude),
            new Coordinate(CoMinLongitude, CoMaxLatitude),
            new Coordinate(CoMinLongitude, CoMinLatitude)
        }))
        { SRID = 4326 };

        var foundTolls = new List<ColoradoFoundTollInfo>();
        var notFoundPlazas = new List<string>();

        foreach (var plaza in plazas)
        {
            if (string.IsNullOrWhiteSpace(plaza.PlazaName))
            {
                continue;
            }

            // Ищем tolls по точному совпадению plaza_name с Name (один в один) в пределах Colorado
            var tolls = await FindTollsInColorado(plaza.PlazaName, coBoundingBox, ct);

            if (tolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.PlazaName);
            }
            else
            {
                foreach (var toll in tolls)
                {
                    // Устанавливаем цены из JSON
                    SetPricesFromJson(toll, plaza.Rates);

                    foundTolls.Add(new ColoradoFoundTollInfo(
                        PlazaName: plaza.PlazaName,
                        TollId: toll.Id,
                        TollName: toll.Name,
                        TollKey: toll.Key,
                        TollNumber: toll.Number));
                }
            }
        }

        // Сохраняем изменения в БД
       await _context.SaveChangesAsync(ct);

        return new LinkColoradoTollsResult(foundTolls, notFoundPlazas);
    }

    private async Task<List<Toll>> FindTollsInColorado(string plazaName, Polygon boundingBox, CancellationToken ct)
    {
        // Ищем точное совпадение plaza_name с Name (один в один, без учета регистра)
        var plazaNameLower = plazaName.ToLower();
        var tolls = await _context.Tolls
            .Include(t => t.TollPrices)
            .Where(t =>
                t.Location != null &&
                boundingBox.Contains(t.Location) &&
                t.Name != null &&
                t.Name.ToLower() == plazaNameLower)
            .ToListAsync(ct);

        return tolls;
    }

    private void SetPricesFromJson(Toll toll, JsonElement? ratesElement)
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
            SetPricesFromE470Format(toll, rates);
        }
        else if (isNorthwayFormat)
        {
            SetPricesFromNorthwayFormat(toll, rates);
        }
    }

    private void SetPricesFromE470Format(Toll toll, JsonElement rates)
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
                    toll.SetPriceByPaymentType(amount, TollPaymentType.PayOnline, axelType);
                }
            }

            // expresstoll_12pm_to_9am -> IPass, Night (12pm-9am это ночь)
            if (axlesRates.TryGetProperty("expresstoll_12pm_to_9am", out var expressToll12pmTo9am) &&
                expressToll12pmTo9am.ValueKind == JsonValueKind.Number)
            {
                var amount = expressToll12pmTo9am.GetDouble();
                if (amount > 0)
                {
                    toll.SetPriceByPaymentType(amount, TollPaymentType.IPass, axelType, 
                        TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any, TollPriceTimeOfDay.Night);
                }
            }

            // expresstoll_9am_to_12pm -> IPass, Day (9am-12pm это день)
            if (axlesRates.TryGetProperty("expresstoll_9am_to_12pm", out var expressToll9amTo12pm) &&
                expressToll9amTo12pm.ValueKind == JsonValueKind.Number)
            {
                var amount = expressToll9amTo12pm.GetDouble();
                if (amount > 0)
                {
                    toll.SetPriceByPaymentType(amount, TollPaymentType.IPass, axelType,
                        TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any, TollPriceTimeOfDay.Day);
                }
            }
        }
    }

    private void SetPricesFromNorthwayFormat(Toll toll, JsonElement rates)
    {
        // Обрабатываем 5_axles и 6_axles
        foreach (var axlesKey in new[] { "5_axles", "6_axles" })
        {
            if (!rates.TryGetProperty(axlesKey, out var axlesRates))
            {
                continue;
            }

            var axelType = axlesKey == "5_axles" ? AxelType._5L : AxelType._6L;

            // rate_6am_to_9pm -> PayOnline, Day
            if (axlesRates.TryGetProperty("rate_6am_to_9pm", out var rate6amTo9pm) &&
                rate6amTo9pm.ValueKind == JsonValueKind.Number)
            {
                var amount = rate6amTo9pm.GetDouble();
                if (amount > 0)
                {
                    toll.SetPriceByPaymentType(amount, TollPaymentType.PayOnline, axelType,
                        TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any, TollPriceTimeOfDay.Day);
                }
            }

            // rate_9pm_to_6am -> PayOnline, Night
            if (axlesRates.TryGetProperty("rate_9pm_to_6am", out var rate9pmTo6am) &&
                rate9pmTo6am.ValueKind == JsonValueKind.Number)
            {
                var amount = rate9pmTo6am.GetDouble();
                if (amount > 0)
                {
                    toll.SetPriceByPaymentType(amount, TollPaymentType.PayOnline, axelType,
                        TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any, TollPriceTimeOfDay.Night);
                }
            }
        }
    }
}

