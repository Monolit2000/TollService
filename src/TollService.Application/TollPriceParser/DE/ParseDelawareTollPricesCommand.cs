using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.DE;

public record ParseDelawareTollPricesCommand(string JsonPayload)
    : IRequest<ParseDelawareTollPricesResult>;

public record DelawareTollRoute(
    [property: System.Text.Json.Serialization.JsonPropertyName("direction")] string Direction,
    [property: System.Text.Json.Serialization.JsonPropertyName("entry")] int Entry,
    [property: System.Text.Json.Serialization.JsonPropertyName("exit")] int Exit,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass")] double EzPass,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] double Cash);

public record DelawareTollResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("state")] string? State,
    [property: System.Text.Json.Serialization.JsonPropertyName("road")] string? Road,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_class")] string? Vehicle_Class,
    [property: System.Text.Json.Serialization.JsonPropertyName("time_period")] string? Time_Period,
    [property: System.Text.Json.Serialization.JsonPropertyName("routes")] List<DelawareTollRoute>? Routes);

public record ParseDelawareTollPricesResult(
    List<string> Lines,
    int UpdatedCount,
    int CreatedCount,
    List<string> NotFoundPlazas,
    string? Error = null);

public class ParseDelawareTollPricesCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseDelawareTollPricesCommand, ParseDelawareTollPricesResult>
{
    // Delaware bounds: (south, west, north, east) = (38.4, -75.8, 39.7, -75.0)
    private static readonly double DeMinLatitude = 38.4;
    private static readonly double DeMinLongitude = -75.8;
    private static readonly double DeMaxLatitude = 39.7;
    private static readonly double DeMaxLongitude = -75.0;
    public async Task<ParseDelawareTollPricesResult> Handle(ParseDelawareTollPricesCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new ParseDelawareTollPricesResult(new(), 0, 0, new(), "JSON payload is empty");
        }

        DelawareTollResponse? data;
        try
        {
            await Task.Yield(); // keep async signature
            data = JsonSerializer.Deserialize<DelawareTollResponse>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new ParseDelawareTollPricesResult(new(), 0, 0, new(), $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.Routes == null || data.Routes.Count == 0)
        {
            return new ParseDelawareTollPricesResult(new(), 0, 0, new(), "Маршруты не найдены в ответе");
        }

        // Получаем или создаем StateCalculator для Delaware
        var delawareCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "DE", ct);

        if (delawareCalculator == null)
        {
            delawareCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "Delaware Turnpike",
                StateCode = "DE"
            };
            _context.StateCalculators.Add(delawareCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // Создаем bounding box для Delaware
        var delawareBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(DeMinLongitude, DeMinLatitude),
            new Coordinate(DeMaxLongitude, DeMinLatitude),
            new Coordinate(DeMaxLongitude, DeMaxLatitude),
            new Coordinate(DeMinLongitude, DeMaxLatitude),
            new Coordinate(DeMinLongitude, DeMinLatitude)
        }))
        { SRID = 4326 };

        // Сначала проходимся по всем toll'ам в пределах Delaware и устанавливаем им Number из Name или Key
        // Формат: "165-Southbound" -> Number = "165" или просто "129" -> Number = "129"
        var allTolls = await _context.Tolls
            .Where(t => t.Location != null && delawareBoundingBox.Contains(t.Location))
            .ToListAsync(ct);

        // Фильтруем в памяти: toll'ы с форматом "number-direction" или просто число
        var tollsToProcess = allTolls
            .Where(t =>
                // Toll'ы с форматом "number-direction"
                (t.Name != null && (t.Name.Contains("-Northbound") || t.Name.Contains("-Southbound"))) ||
                (t.Key != null && (t.Key.Contains("-Northbound") || t.Key.Contains("-Southbound"))) ||
                // Или toll'ы, у которых Name/Key - просто число
                (t.Name != null && int.TryParse(t.Name.Trim(), out _)) ||
                (t.Key != null && int.TryParse(t.Key.Trim(), out _)))
            .ToList();

        foreach (var toll in tollsToProcess)
        {
            if (string.IsNullOrWhiteSpace(toll.Number))
            {
                // Извлекаем номер из Name или Key
                var source = toll.Name ?? toll.Key ?? string.Empty;
                var number = ExtractPlazaNumber(source);
                
                // Если не получилось извлечь (нет дефиса), пробуем как число
                if (string.IsNullOrWhiteSpace(number) && int.TryParse(source.Trim(), out _))
                {
                    number = source.Trim();
                }
                
                if (!string.IsNullOrWhiteSpace(number))
                {
                    toll.Number = number;
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        var resultLines = new List<string>();
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;
        int createdCount = 0;

        // Создаем словарь для кэширования найденных tolls по номеру плазы
        var tollCache = new Dictionary<string, Toll?>();

        // Обрабатываем каждый маршрут
        foreach (var route in data.Routes)
        {
            // Ищем toll'ы по номеру плазы (без учета направления)
            var entryNumber = route.Entry.ToString();
            var exitNumber = route.Exit.ToString();

            // Находим toll для entry (точка входа)
            var fromToll = await FindOrCacheTollByNumber(entryNumber, tollCache, delawareCalculator.Id, delawareBoundingBox, ct);
            if (fromToll == null)
            {
                notFoundPlazas.Add($"Entry: {entryNumber}");
                resultLines.Add($"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction} - НЕ НАЙДЕНО");
                continue;
            }

            // Находим toll для exit (точка выхода)
            var toToll = await FindOrCacheTollByNumber(exitNumber, tollCache, delawareCalculator.Id, delawareBoundingBox, ct);
            if (toToll == null)
            {
                notFoundPlazas.Add($"Exit: {exitNumber}");
                resultLines.Add($"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction} - НЕ НАЙДЕНО");
                continue;
            }

            // Проверяем, существует ли уже CalculatePrice для этой пары
            var existingPrice = await _context.CalculatePrices
                .FirstOrDefaultAsync(cp =>
                    cp.FromId == fromToll.Id &&
                    cp.ToId == toToll.Id &&
                    cp.StateCalculatorId == delawareCalculator.Id, ct);

            if (existingPrice != null)
            {
                // Обновляем существующую запись
                existingPrice.Cash = route.Cash;
                existingPrice.IPass = route.EzPass;
                existingPrice.Online = route.Cash; // Online обычно равно Cash
                updatedCount++;
            }
            else
            {
                // Создаем новую запись
                var calculatePrice = new CalculatePrice
                {
                    Id = Guid.NewGuid(),
                    StateCalculatorId = delawareCalculator.Id,
                    FromId = fromToll.Id,
                    ToId = toToll.Id,
                    Cash = route.Cash,
                    IPass = route.EzPass,
                    Online = route.Cash
                };
                _context.CalculatePrices.Add(calculatePrice);
                createdCount++;
            }

            // Добавляем строку в результат
            resultLines.Add($"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction} - {route.EzPass:0.00}/{route.Cash:0.00}");
        }

        await _context.SaveChangesAsync(ct);

        return new ParseDelawareTollPricesResult(
            resultLines,
            updatedCount,
            createdCount,
            notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Извлекает номер плазы из строки формата "{number}-{direction}"
    /// </summary>
    private static string ExtractPlazaNumber(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        // Ищем паттерн: число перед дефисом
        var parts = source.Split('-');
        if (parts.Length > 0)
        {
            return parts[0].Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// Находит Toll по номеру плазы, используя кэш для оптимизации, и устанавливает StateCalculatorId
    /// Сначала ищет по Key/Name, затем по Number, в пределах bounding box штата Delaware
    /// </summary>
    private async Task<Toll?> FindOrCacheTollByNumber(string number, Dictionary<string, Toll?> cache, Guid stateCalculatorId, Polygon delawareBoundingBox, CancellationToken ct)
    {
        if (cache.TryGetValue(number, out var cachedToll))
        {
            // Если toll уже найден, но StateCalculatorId не установлен, обновляем его
            if (cachedToll != null && cachedToll.StateCalculatorId != stateCalculatorId)
            {
                cachedToll.StateCalculatorId = stateCalculatorId;
            }
            return cachedToll;
        }

        // Сначала ищем по Key или Name, которые содержат этот номер, в пределах Delaware
        var toll = await _context.Tolls
            .FirstOrDefaultAsync(t => 
                t.Location != null &&
                delawareBoundingBox.Contains(t.Location) &&
                (
                    (t.Key != null && (t.Key == number || t.Key.StartsWith(number + "-") || t.Key.EndsWith("-" + number))) ||
                    (t.Name != null && (t.Name == number || t.Name.StartsWith(number + "-") || t.Name.EndsWith("-" + number)))
                ), ct);

        // Если не нашли по Key/Name, ищем по Number в пределах Delaware
        if (toll == null)
        {
            toll = await _context.Tolls
                .FirstOrDefaultAsync(t => 
                    t.Number == number &&
                    t.Location != null &&
                    delawareBoundingBox.Contains(t.Location), ct);
        }

        // Если нашли по Key/Name, устанавливаем Number для будущих поисков
        if (toll != null && string.IsNullOrWhiteSpace(toll.Number))
        {
            toll.Number = number;
        }

        // Если toll найден, устанавливаем StateCalculatorId
        if (toll != null)
        {
            toll.StateCalculatorId = stateCalculatorId;
        }

        cache[number] = toll;
        return toll;
    }
}

