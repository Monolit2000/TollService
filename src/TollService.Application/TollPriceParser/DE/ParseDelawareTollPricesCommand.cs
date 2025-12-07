using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
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
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseDelawareTollPricesCommand, ParseDelawareTollPricesResult>
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
        var delawareCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
            stateCode: "DE",
            calculatorName: "Delaware Turnpike",
            ct);

        // Создаем bounding box для Delaware
        var delawareBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            DeMinLongitude,
            DeMinLatitude,
            DeMaxLongitude,
            DeMaxLatitude);

        // Number и StateCalculatorId будут установлены в методе FindOrCacheTollsByNumber при поиске tolls по маршрутам

        var resultLines = new List<string>();
        var notFoundPlazas = new List<string>();
        var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

        // Создаем словарь для кэширования найденных tolls по номеру плазы
        var tollCache = new Dictionary<string, List<Toll>>();

        // Обрабатываем каждый маршрут
        foreach (var route in data.Routes)
        {
            // Ищем toll'ы по номеру плазы (без учета направления)
            var entryNumber = route.Entry.ToString();
            var exitNumber = route.Exit.ToString();

            // Находим все tolls для entry (может быть несколько: Northbound, Southbound)
            var entryTolls = await FindOrCacheTollsByNumber(entryNumber, tollCache, delawareCalculator.Id, delawareBoundingBox, ct);
            if (entryTolls.Count == 0)
            {
                notFoundPlazas.Add($"Entry: {entryNumber}");
                resultLines.Add($"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction} - НЕ НАЙДЕНО");
                continue;
            }

            // Находим все tolls для exit (может быть несколько: Northbound, Southbound)
            var exitTolls = await FindOrCacheTollsByNumber(exitNumber, tollCache, delawareCalculator.Id, delawareBoundingBox, ct);
            if (exitTolls.Count == 0)
            {
                notFoundPlazas.Add($"Exit: {exitNumber}");
                resultLines.Add($"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction} - НЕ НАЙДЕНО");
                continue;
            }


            var description = $"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction}";

            // Обрабатываем все комбинации entry -> exit толлов
            var pairResults = TollPairProcessor.ProcessAllPairsToDictionaryList(
                entryTolls,
                exitTolls,
                (entryToll, exitToll) =>
                {
                    var tollPrices = new List<TollPrice>();

                    // Создаем TollPrice для EZPass
                    if (route.EzPass > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.EZPass,
                            AxelType = AxelType._5L,
                            Amount = route.EzPass,
                            Description = description
                        });
                    }

                    // Создаем TollPrice для Cash
                    if (route.Cash > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.Cash,
                            AxelType = AxelType._5L,
                            Amount = route.Cash,
                            Description = description
                        });
                    }

                    return tollPrices;
                });

            // Объединяем результаты с общим словарем
            foreach (var kvp in pairResults)
            {
                if (!tollPairsWithPrices.TryGetValue(kvp.Key, out var existingList))
                {
                    existingList = new List<TollPrice>();
                    tollPairsWithPrices[kvp.Key] = existingList;
                }
                existingList.AddRange(kvp.Value);
            }

            // Добавляем строку в результат
            resultLines.Add($"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction} - {route.EzPass:0.00}/{route.Cash:0.00}");
        }

        // Батч-создание/обновление CalculatePrice с TollPrice
        int createdCount = 0;
        int updatedCount = 0;

        if (tollPairsWithPrices.Count > 0)
        {
            var tollPairsWithPricesEnumerable = tollPairsWithPrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPrice>?)kvp.Value);

            // Загружаем только те пары, которые мы обрабатываем
            var pairsToCheck = tollPairsWithPrices.Keys.ToList();
            var fromIds = pairsToCheck.Select(p => p.FromId).Distinct().ToList();
            var toIds = pairsToCheck.Select(p => p.ToId).Distinct().ToList();

            var existingPrices = await _context.CalculatePrices
                .Where(cp =>
                    cp.StateCalculatorId == delawareCalculator.Id &&
                    fromIds.Contains(cp.FromId) &&
                    toIds.Contains(cp.ToId))
                .Select(cp => new { cp.FromId, cp.ToId })
                .ToListAsync(ct);

            var existingKeys = new HashSet<(Guid FromId, Guid ToId)>(existingPrices.Select(p => (p.FromId, p.ToId)));

            var calculatedPrices = await _calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                tollPairsWithPricesEnumerable,
                delawareCalculator.Id,
                includeTollPrices: true,
                ct);

            foreach (var kvp in calculatedPrices)
            {
                if (existingKeys.Contains(kvp.Key))
                {
                    updatedCount++;
                }
                else
                {
                    createdCount++;
                }
            }
        }

        // Сохраняем все изменения (Toll, CalculatePrice, TollPrice)
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
    /// Находит все Toll'ы по номеру плазы, используя кэш для оптимизации.
    /// Сначала ищет по Key/Name через TollSearchService, затем по Number, в пределах bounding box штата Delaware.
    /// Может вернуть несколько толлов (например, для Northbound и Southbound).
    /// </summary>
    private async Task<List<Toll>> FindOrCacheTollsByNumber(
        string number,
        Dictionary<string, List<Toll>> cache,
        Guid stateCalculatorId,
        Polygon delawareBoundingBox,
        CancellationToken ct)
    {
        if (cache.TryGetValue(number, out var cachedTolls))
        {
            // Number и StateCalculatorId уже установлены при первом поиске
            return cachedTolls;
        }

        var tolls = new List<Toll>();

        // Сначала ищем по Key/Name через TollSearchService (поиск по строке номера найдет "165-Southbound", "165-Northbound", "165")
        var tollsByKeyName = await _tollSearchService.FindTollsInBoundingBoxAsync(
            number,
            delawareBoundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        // Фильтруем результаты: должны содержать номер (точное совпадение, начинается с номера или заканчивается на номер)
        var filteredTolls = tollsByKeyName
            .Where(t =>
                (t.Key != null && (t.Key == number || t.Key.StartsWith(number + "-") || t.Key.EndsWith("-" + number))) ||
                (t.Name != null && (t.Name == number || t.Name.StartsWith(number + "-") || t.Name.EndsWith("-" + number))))
            .ToList();

        tolls.AddRange(filteredTolls);

        // Если не нашли по Key/Name, ищем по Number в пределах Delaware
        if (tolls.Count == 0)
        {
            var tollsByNumber = await _context.Tolls
                .Where(t =>
                    t.Number == number &&
                    t.Location != null &&
                    delawareBoundingBox.Contains(t.Location))
                .ToListAsync(ct);

            tolls.AddRange(tollsByNumber);
        }

        // Устанавливаем Number и StateCalculatorId для всех найденных tolls
        _tollNumberService.SetNumberAndCalculatorId(
            tolls,
            number,
            stateCalculatorId,
            updateNumberIfDifferent: false);

        cache[number] = tolls;
        return tolls;
    }
}

