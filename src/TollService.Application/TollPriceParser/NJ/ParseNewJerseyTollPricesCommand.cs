using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record ParseNewJerseyTollPricesCommand(string JsonPayload)
    : IRequest<ParseNewJerseyTollPricesResult>;

public record NewJerseyTollRate(
    [property: JsonPropertyName("entry")] string Entry,
    [property: JsonPropertyName("exit")] string Exit,
    [property: JsonPropertyName("entry_name")] string? EntryName,
    [property: JsonPropertyName("exit_name")] string? ExitName,
    [property: JsonPropertyName("cash")] double Cash,
    [property: JsonPropertyName("ez_pass_peak")] double EzPassPeak,
    [property: JsonPropertyName("ez_pass_off_peak")] double EzPassOffPeak,
    [property: JsonPropertyName("status")] string? Status);

public record NewJerseyTollResponse(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("road")] string? Road,
    [property: JsonPropertyName("vehicle_class_id")] int? VehicleClassId,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("total_checked")] int? TotalChecked,
    [property: JsonPropertyName("toll_rates")] List<NewJerseyTollRate>? TollRates);

public record ParseNewJerseyTollPricesResult(
    List<string> Lines,
    int UpdatedCount,
    int CreatedCount,
    List<string> NotFoundPlazas,
    string? Error = null);

// New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
public class ParseNewJerseyTollPricesCommandHandler(
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseNewJerseyTollPricesCommand, ParseNewJerseyTollPricesResult>
{
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
        var newJerseyCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
            stateCode: "NJ",
            calculatorName: "New Jersey Turnpike",
            ct);

        // Создаем bounding box для New Jersey
        var njBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            NjMinLongitude,
            NjMinLatitude,
            NjMaxLongitude,
            NjMaxLatitude);

        var resultLines = new List<string>();
        var notFoundPlazas = new List<string>();
        var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

        // Создаем словарь для кэширования найденных tolls по номеру плазы
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

            // Находим все tolls для entry (может быть несколько)
            var entryTolls = await FindOrCacheTollsByNumber(entryNumber, tollCache, newJerseyCalculator.Id, njBoundingBox, ct);
            if (entryTolls.Count == 0)
            {
                notFoundPlazas.Add($"Entry: {entryNumber}");
                resultLines.Add($"{entryNumber} -> {exitNumber} - НЕ НАЙДЕНО");
                continue;
            }

            // Находим все tolls для exit (может быть несколько)
            var exitTolls = await FindOrCacheTollsByNumber(exitNumber, tollCache, newJerseyCalculator.Id, njBoundingBox, ct);
            if (exitTolls.Count == 0)
            {
                notFoundPlazas.Add($"Exit: {exitNumber}");
                resultLines.Add($"{entryNumber} -> {exitNumber} - НЕ НАЙДЕНО");
                continue;
            }

            var description = $"{entryNumber} -> {exitNumber}";

            // Обрабатываем все комбинации entry -> exit толлов
            var pairResults = TollPairProcessor.ProcessAllPairsToDictionaryList(
                entryTolls,
                exitTolls,
                (entryToll, exitToll) =>
                {
                    var tollPrices = new List<TollPrice>();

                    // Создаем TollPrice для Cash
                    if (rate.Cash > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.Cash,
                            AxelType = AxelType._5L,
                            Amount = rate.Cash,
                            Description = description
                        });
                    }

                    // Создаем TollPrice для EZPass Peak
                    if (rate.EzPassPeak > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.EZPass,
                            AxelType = AxelType._5L,
                            Amount = rate.EzPassPeak,
                            Description = $"{description} (Peak)"
                        });
                    }

                    // Создаем TollPrice для EZPass Off-Peak
                    if (rate.EzPassOffPeak > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.EZPass,
                            AxelType = AxelType._5L,
                            Amount = rate.EzPassOffPeak,
                            Description = $"{description} (Off-Peak)"
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
            resultLines.Add($"{entryNumber} -> {exitNumber} - Cash: {rate.Cash:0.00}, EZPass Off-Peak: {rate.EzPassOffPeak:0.00}, EZPass Peak: {rate.EzPassPeak:0.00}");
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
                    cp.StateCalculatorId == newJerseyCalculator.Id &&
                    fromIds.Contains(cp.FromId) &&
                    toIds.Contains(cp.ToId))
                .Select(cp => new { cp.FromId, cp.ToId })
                .ToListAsync(ct);

            var existingKeys = new HashSet<(Guid FromId, Guid ToId)>(existingPrices.Select(p => (p.FromId, p.ToId)));

            var calculatedPrices = await _calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                tollPairsWithPricesEnumerable,
                newJerseyCalculator.Id,
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

        return new ParseNewJerseyTollPricesResult(
            resultLines,
            updatedCount,
            createdCount,
            notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Находит все Toll'ы по номеру плазы, используя кэш для оптимизации.
    /// Сначала ищет по Key/Name через TollSearchService, затем по Number, в пределах bounding box штата New Jersey.
    /// Может вернуть несколько толлов.
    /// </summary>
    private async Task<List<Toll>> FindOrCacheTollsByNumber(
        string number,
        Dictionary<string, List<Toll>> cache,
        Guid stateCalculatorId,
        Polygon njBoundingBox,
        CancellationToken ct)
    {
        if (cache.TryGetValue(number, out var cachedTolls))
        {
            // Number и StateCalculatorId уже установлены при первом поиске
            return cachedTolls;
        }

        var tolls = new List<Toll>();

        // Сначала ищем по Key/Name через TollSearchService
        var tollsByKeyName = await _tollSearchService.FindTollsInBoundingBoxAsync(
            number,
            njBoundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        // Фильтруем результаты: должны содержать номер (точное совпадение, начинается с номера или заканчивается на номер)
        var filteredTolls = tollsByKeyName
            .Where(t =>
                (t.Key != null && (t.Key == number || t.Key.StartsWith(number + "-") || t.Key.EndsWith("-" + number))) ||
                (t.Name != null && (t.Name == number || t.Name.StartsWith(number + "-") || t.Name.EndsWith("-" + number))))
            .ToList();

        tolls.AddRange(filteredTolls);

        // Если не нашли по Key/Name, ищем по Number в пределах New Jersey
        if (tolls.Count == 0)
        {
            var tollsByNumber = await _context.Tolls
                .Where(t =>
                    t.Number == number &&
                    t.Location != null &&
                    njBoundingBox.Contains(t.Location))
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

