using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record ParseNewJerseyTollPricesCommand(string JsonPayload)
    : IRequest<ParseNewJerseyTollPricesResult>;

public record NewJerseyTollRate(
    [property: System.Text.Json.Serialization.JsonPropertyName("entry")] string Entry,
    [property: System.Text.Json.Serialization.JsonPropertyName("exit")] string Exit,
    [property: System.Text.Json.Serialization.JsonPropertyName("entry_name")] string? EntryName,
    [property: System.Text.Json.Serialization.JsonPropertyName("exit_name")] string? ExitName,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] double Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass_peak")] double EzPassPeak,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass_off_peak")] double EzPassOffPeak,
    [property: System.Text.Json.Serialization.JsonPropertyName("status")] string? Status);

public record NewJerseyTollResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("state")] string? State,
    [property: System.Text.Json.Serialization.JsonPropertyName("road")] string? Road,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_class_id")] int? VehicleClassId,
    [property: System.Text.Json.Serialization.JsonPropertyName("description")] string? Description,
    [property: System.Text.Json.Serialization.JsonPropertyName("total_checked")] int? TotalChecked,
    [property: System.Text.Json.Serialization.JsonPropertyName("toll_rates")] List<NewJerseyTollRate>? TollRates);

public record ParseNewJerseyTollPricesResult(
    List<string> Lines,
    int UpdatedCount,
    int CreatedCount,
    List<string> NotFoundPlazas,
    string? Error = null);

public class ParseNewJerseyTollPricesCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseNewJerseyTollPricesCommand, ParseNewJerseyTollPricesResult>
{
    // New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
    private static readonly double NjMinLatitude = 38.9;
    private static readonly double NjMinLongitude = -75.6;
    private static readonly double NjMaxLatitude = 41.4;
    private static readonly double NjMaxLongitude = -73.9;

    public async Task<ParseNewJerseyTollPricesResult> Handle(ParseNewJerseyTollPricesCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new ParseNewJerseyTollPricesResult(new(), 0, 0, new(), "JSON payload is empty");
        }

        NewJerseyTollResponse? data;
        try
        {
            await Task.Yield();
            data = JsonSerializer.Deserialize<NewJerseyTollResponse>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new ParseNewJerseyTollPricesResult(new(), 0, 0, new(), $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.TollRates == null || data.TollRates.Count == 0)
        {
            return new ParseNewJerseyTollPricesResult(new(), 0, 0, new(), "Тарифы не найдены в ответе");
        }

        // Получаем или создаем StateCalculator для New Jersey
        var njCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "NJ", ct);

        if (njCalculator == null)
        {
            njCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "New Jersey Turnpike",
                StateCode = "NJ"
            };
            _context.StateCalculators.Add(njCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // Создаем bounding box для New Jersey
        var njBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(NjMinLongitude, NjMinLatitude),
            new Coordinate(NjMaxLongitude, NjMinLatitude),
            new Coordinate(NjMaxLongitude, NjMaxLatitude),
            new Coordinate(NjMinLongitude, NjMaxLatitude),
            new Coordinate(NjMinLongitude, NjMinLatitude)
        }))
        { SRID = 4326 };

        var resultLines = new List<string>();
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;
        int createdCount = 0;

        // Создаем словарь для кэширования найденных tolls по номеру плазы (список всех toll'ов с таким номером)
        var tollCache = new Dictionary<string, List<Toll>>();

        // Обрабатываем каждый тариф
        foreach (var rate in data.TollRates)
        {
            // Пропускаем записи со статусом не OK
            if (rate.Status != null && rate.Status != "OK")
                continue;

            // Ищем toll'ы по номеру плазы (entry и exit - строки, например "01", "07A", "14C")
            var entryNumber = rate.Entry;
            var exitNumber = rate.Exit;

            // Находим все toll'ы для entry (точка входа)
            var fromTolls = await FindOrCacheAllTollsByNumber(entryNumber, tollCache, njCalculator.Id, njBoundingBox, ct);
            if (fromTolls.Count == 0)
            {
                notFoundPlazas.Add($"Entry: {entryNumber}");
                resultLines.Add($"{entryNumber} -> {exitNumber} - НЕ НАЙДЕНО");
                continue;
            }

            // Находим все toll'ы для exit (точка выхода)
            var toTolls = await FindOrCacheAllTollsByNumber(exitNumber, tollCache, njCalculator.Id, njBoundingBox, ct);
            if (toTolls.Count == 0)
            {
                notFoundPlazas.Add($"Exit: {exitNumber}");
                resultLines.Add($"{entryNumber} -> {exitNumber} - НЕ НАЙДЕНО");
                continue;
            }

            // Создаем записи для всех комбинаций entry -> exit
            foreach (var fromToll in fromTolls)
            {
                foreach (var toToll in toTolls)
                {
                    // Пропускаем, если entry и exit - один и тот же toll
                    if (fromToll.Id == toToll.Id)
                        continue;

                    // Проверяем, существует ли уже CalculatePrice для этой пары
                    var existingPrice = await _context.CalculatePrices
                        .FirstOrDefaultAsync(cp =>
                            cp.FromId == fromToll.Id &&
                            cp.ToId == toToll.Id &&
                            cp.StateCalculatorId == njCalculator.Id, ct);

                    if (existingPrice != null)
                    {
                        // Обновляем существующую запись
                        existingPrice.Cash = rate.Cash;
                        existingPrice.IPass = rate.EzPassOffPeak; // Используем off-peak как базовую цену EZPass
                        existingPrice.Online = rate.Cash; // Online обычно равно Cash
                        updatedCount++;
                    }
                    else
                    {
                        // Создаем новую запись
                        var calculatePrice = new CalculatePrice
                        {
                            Id = Guid.NewGuid(),
                            StateCalculatorId = njCalculator.Id,
                            FromId = fromToll.Id,
                            ToId = toToll.Id,
                            Cash = rate.Cash,
                            IPass = rate.EzPassOffPeak,
                            Online = rate.Cash
                        };
                        _context.CalculatePrices.Add(calculatePrice);
                        createdCount++;
                    }
                }
            }

            // Добавляем строку в результат
            resultLines.Add($"{entryNumber} -> {exitNumber} - Cash: {rate.Cash:0.00}, EZPass Off-Peak: {rate.EzPassOffPeak:0.00}, EZPass Peak: {rate.EzPassPeak:0.00} (создано {fromTolls.Count * toTolls.Count} записей)");
        }

        await _context.SaveChangesAsync(ct);

        return new ParseNewJerseyTollPricesResult(
            resultLines,
            updatedCount,
            createdCount,
            notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Находит все Toll'ы по номеру плазы, используя кэш для оптимизации, и устанавливает StateCalculatorId
    /// Сначала ищет по Key/Name, затем по Number, в пределах bounding box штата New Jersey
    /// </summary>
    private async Task<List<Toll>> FindOrCacheAllTollsByNumber(string number, Dictionary<string, List<Toll>> cache, Guid stateCalculatorId, Polygon njBoundingBox, CancellationToken ct)
    {
        if (cache.TryGetValue(number, out var cachedTolls))
        {
            // Устанавливаем StateCalculatorId для всех toll'ов в кэше
            foreach (var toll in cachedTolls)
            {
                if (toll.StateCalculatorId != stateCalculatorId)
                {
                    toll.StateCalculatorId = stateCalculatorId;
                }
            }
            return cachedTolls;
        }

        var tolls = new List<Toll>();

        // Сначала ищем по Key или Name, которые содержат этот номер, в пределах New Jersey
        var tollsByKeyName = await _context.Tolls
            .Where(t => 
                t.Location != null &&
                njBoundingBox.Contains(t.Location) &&
                (
                    (t.Key != null && (t.Key == number || t.Key.StartsWith(number + "-") || t.Key.EndsWith("-" + number))) ||
                    (t.Name != null && (t.Name == number || t.Name.StartsWith(number + "-") || t.Name.EndsWith("-" + number)))
                ))
            .ToListAsync(ct);

        tolls.AddRange(tollsByKeyName);

        // Ищем по Number в пределах New Jersey (исключаем уже найденные)
        var existingIds = tolls.Select(t => t.Id).ToHashSet();
        var tollsByNumber = await _context.Tolls
            .Where(t => 
                t.Number == number &&
                t.Location != null &&
                njBoundingBox.Contains(t.Location) &&
                !existingIds.Contains(t.Id))
            .ToListAsync(ct);

        tolls.AddRange(tollsByNumber);

        // Устанавливаем Number для тех, у кого его нет
        foreach (var toll in tolls)
        {
            if (string.IsNullOrWhiteSpace(toll.Number))
            {
                toll.Number = number;
            }
            
            // Устанавливаем StateCalculatorId
            toll.StateCalculatorId = stateCalculatorId;
        }

        cache[number] = tolls;
        return tolls;
    }
}

