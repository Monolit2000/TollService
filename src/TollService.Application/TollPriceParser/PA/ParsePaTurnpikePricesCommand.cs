using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.PA;

public record ParsePaTurnpikePricesCommand(
    string? TollType = null,
    string? RoadwayKey = null,
    string? EntryInterchangeKey = null,
    string? EffectiveDateKey = null,
    string BaseUrl = "https://www.paturnpike.com/toll-schedule-v2/get-toll-schedule")
    : IRequest<ParseTollPricesResult>;

public record PaTurnpikeTollScheduleResponse(
    [property: JsonPropertyName("StatusCode")] int StatusCode,
    [property: JsonPropertyName("Success")] bool Success,
    [property: JsonPropertyName("Data")] PaTurnpikeScheduleData? Data);

public record PaTurnpikeScheduleData(
    [property: JsonPropertyName("ErrorCount")] int ErrorCount,
    [property: JsonPropertyName("HasErrors")] bool HasErrors,
    [property: JsonPropertyName("ResultObject")] List<PaTurnpikeTollEntry>? ResultObject);

public record PaTurnpikeTollEntry(
    [property: JsonPropertyName("ExitInterchangeId")] double ExitInterchangeId,
    [property: JsonPropertyName("ExitInterchangeName")] string? ExitInterchangeName,
    [property: JsonPropertyName("ExitInterchange")] string? ExitInterchange,
    [property: JsonPropertyName("ExitInterchangeKey")] int ExitInterchangeKey,
    [property: JsonPropertyName("L2Axle")] double L2Axle,
    [property: JsonPropertyName("H2Axle")] double H2Axle,
    [property: JsonPropertyName("L3Axle")] double L3Axle,
    [property: JsonPropertyName("H3Axle")] double H3Axle,
    [property: JsonPropertyName("L4Axle")] double L4Axle,
    [property: JsonPropertyName("H4Axle")] double H4Axle,
    [property: JsonPropertyName("L5Axle")] double L5Axle,
    [property: JsonPropertyName("H5Axle")] double H5Axle,
    [property: JsonPropertyName("L6Axle")] double L6Axle,
    [property: JsonPropertyName("H6Axle")] double H6Axle,
    [property: JsonPropertyName("L7Axle")] double L7Axle,
    [property: JsonPropertyName("H7Axle")] double H7Axle,
    [property: JsonPropertyName("L8Axle")] double L8Axle,
    [property: JsonPropertyName("H8Axle")] double H8Axle,
    [property: JsonPropertyName("IsSelected")] bool IsSelected);

public class ParsePaTurnpikePricesCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory) : IRequestHandler<ParsePaTurnpikePricesCommand, ParseTollPricesResult>
{
    // Pennsylvania bounds: (south, west, north, east) = (39.7, -80.5, 42.3, -74.7)
    private static readonly double PaMinLatitude = 39.7;
    private static readonly double PaMinLongitude = -80.5;
    private static readonly double PaMaxLatitude = 42.3;
    private static readonly double PaMaxLongitude = -74.7;

    public async Task<ParseTollPricesResult> Handle(ParsePaTurnpikePricesCommand request, CancellationToken ct)
    {
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;

        var tolls = await _context.Tolls.Where(t => t.PaPlazaKay != 0).OrderBy(t => t.PaPlazaKay).ToListAsync(ct);

        var patTypes = new List<string>(){"1", "2"};

        foreach (var paToll in tolls)
        {
            foreach (var payType in patTypes)
            {

                // Строим URL с query параметрами
                var queryParams = new List<string>();
                //if (!string.IsNullOrWhiteSpace(request.TollType))
                    queryParams.Add($"tollType={Uri.EscapeDataString(payType)}");
                //if (!string.IsNullOrWhiteSpace(request.RoadwayKey))
                    queryParams.Add($"roadwayKey={Uri.EscapeDataString("76")}");
                //if (!string.IsNullOrWhiteSpace(request.EntryInterchangeKey))
                queryParams.Add($"entryInterchangeKey={Uri.EscapeDataString(paToll.PaPlazaKay.ToString())}");
                //if (!string.IsNullOrWhiteSpace(request.EffectiveDateKey))
                queryParams.Add($"effectiveDateKey={Uri.EscapeDataString("4")}");

                var url = request.BaseUrl;
                if (queryParams.Count > 0)
                {
                    url += "?" + string.Join("&", queryParams);
                }

                // Получаем данные с API
                var httpClient = _httpClientFactory.CreateClient();
                string jsonContent;
                try
                {
                    jsonContent = await httpClient.GetStringAsync(url, ct);
                }
                catch (Exception ex)
                {
                    return new ParseTollPricesResult(0, new List<string> { $"Ошибка при получении данных: {ex.Message}" });
                }

                // Парсим JSON
                PaTurnpikeTollScheduleResponse? response;
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    response = JsonSerializer.Deserialize<PaTurnpikeTollScheduleResponse>(jsonContent, options);
                    if (response == null || !response.Success || response.Data == null || response.Data.ResultObject == null || response.Data.ResultObject.Count == 0)
                    {
                        return new ParseTollPricesResult(0, new List<string> { "JSON пуст или невалиден, либо ответ не успешен" });
                    }
                }
                catch (JsonException ex)
                {
                    return new ParseTollPricesResult(0, new List<string> { $"Ошибка парсинга JSON: {ex.Message}" });
                }

                // Получаем или создаем StateCalculator для Pennsylvania
                var paCalculator = await _context.StateCalculators
                    .FirstOrDefaultAsync(sc => sc.StateCode == "PA", ct);

                if (paCalculator == null)
                {
                    paCalculator = new StateCalculator
                    {
                        Id = Guid.NewGuid(),
                        Name = "Pennsylvania Turnpike",
                        StateCode = "PA"
                    };
                    _context.StateCalculators.Add(paCalculator);
                    await _context.SaveChangesAsync(ct);
                }

                // Создаем bounding box для Пенсильвании
                var paBoundingBox = new Polygon(new LinearRing(new[]
                {
            new Coordinate(PaMinLongitude, PaMinLatitude),
            new Coordinate(PaMaxLongitude, PaMinLatitude),
            new Coordinate(PaMaxLongitude, PaMaxLatitude),
            new Coordinate(PaMinLongitude, PaMaxLatitude),
            new Coordinate(PaMinLongitude, PaMinLatitude)
        }))
                { SRID = 4326 };

                // Находим entry toll по EntryInterchangeKey (если указан)
                Toll? fromToll = null;
                fromToll = await FindFromTollByNumberInBounds(paToll.PaPlazaKay, paBoundingBox, paCalculator.Id, ct);
                if (fromToll == null)
                {
                    notFoundPlazas.Add($"Entry Interchange Key: {request.EntryInterchangeKey}");
                }

                // Обрабатываем каждую запись о цене
                foreach (var priceEntry in response.Data.ResultObject)
                {
                    // Пропускаем записи с нулевой ценой или IsSelected = true (это entry точка)
                    //if (priceEntry.L2Axle == 0 && priceEntry.H5Axle == 0)
                    //{
                    //    continue;
                    //}

                    // Находим exit toll по ExitInterchangeId (Number)
                    // Округляем дробное значение до ближайшего целого для поиска по Number
                    var exitInterchangeIdInt = priceEntry.ExitInterchangeId.ToString();
                    var toToll = await FindTollByNumberInBounds(priceEntry.ExitInterchangeId.ToString(), paBoundingBox, paCalculator.Id, ct);
                    if (toToll == null)
                    {
                        notFoundPlazas.Add($"Exit Interchange ID: {priceEntry.ExitInterchangeId} ({priceEntry.ExitInterchangeName})");
                        continue;
                    }

                    // Если fromToll не найден, пропускаем эту запись
                    if (fromToll == null)
                    {
                        throw new Exception("Not found From");
                    }

                    // Определяем цены в зависимости от типа
                    // tollType = "1" → только IPass
                    // tollType = "2" → Cash и Online (не IPass)
                    var tollType = request.TollType ?? "2"; // По умолчанию тип 2
                    var price = priceEntry.L5Axle; // Используем L2Axle как базовую цену
                    var cashPrice = priceEntry.H5Axle;

                    // Проверяем, существует ли уже CalculatePrice для этой пары
                    var existingPrice = await _context.CalculatePrices
                        .FirstOrDefaultAsync(cp =>
                            cp.FromId == fromToll.Id &&
                            cp.ToId == toToll.Id &&
                            cp.StateCalculatorId == paCalculator.Id, ct);

                    if (existingPrice != null)
                    {
                        // Обновляем существующую запись в зависимости от типа
                        if (tollType == "1")
                        {
                            // Тип 1: только IPass
                            existingPrice.IPass = price;
                        }
                        else if (tollType == "2")
                        {
                            // Тип 2: Cash и Online (не IPass)
                            existingPrice.Cash = price;
                            existingPrice.Online = price;
                        }
                    }
                    else
                    {
                        // Создаем новую запись в зависимости от типа
                        var calculatePrice = new CalculatePrice
                        {
                            Id = Guid.NewGuid(),
                            StateCalculatorId = paCalculator.Id,
                            FromId = fromToll.Id,
                            ToId = toToll.Id,
                            Cash = tollType == "2" ? cashPrice : 0,
                            IPass = tollType == "1" ? price : 0,
                            Online = tollType == "2" ? price : 0
                        };
                        _context.CalculatePrices.Add(calculatePrice);
                    }

                    updatedCount++;
                }

                await _context.SaveChangesAsync(ct);
            }
        }
        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Находит Toll по Number в пределах гео рамки Пенсильвании
    /// </summary>
    private async Task<Toll?> FindTollByNumberInBounds(string number, Polygon boundingBox, Guid stateCalculatorId, CancellationToken ct)
    {
        var toll = await _context.Tolls
            .FirstOrDefaultAsync(t =>
                t.Number == number &&
                t.Location != null &&
                boundingBox.Contains(t.Location), ct);

        // Если toll найден, устанавливаем StateCalculatorId
        if (toll != null)
        {
            toll.StateCalculatorId = stateCalculatorId;
        }

        return toll;
    }

    private async Task<Toll?> FindFromTollByNumberInBounds(int number, Polygon boundingBox, Guid stateCalculatorId, CancellationToken ct)
    {
        var toll = await _context.Tolls
            .FirstOrDefaultAsync(t =>
                t.PaPlazaKay == number &&
                t.Location != null &&
                boundingBox.Contains(t.Location), ct);

        // Если toll найден, устанавливаем StateCalculatorId
        if (toll != null)
        {
            toll.StateCalculatorId = stateCalculatorId;
        }

        return toll;
    }
}

