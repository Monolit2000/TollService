using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.IN;

public record ParseIndianaTollPricesCommand(string JsonContent) 
    : IRequest<ParseTollPricesResult>;

public record IndianaTollPriceEntry(
    [property: JsonPropertyName("entry")] string Entry,
    [property: JsonPropertyName("exit")] string Exit,
    [property: JsonPropertyName("cash_rate")] string CashRate,
    [property: JsonPropertyName("avi_rate")] string AviRate);

public class ParseIndianaTollPricesCommandHandler(
    ITollDbContext _context,
    CalculatePriceService calculatePriceService) : IRequestHandler<ParseIndianaTollPricesCommand, ParseTollPricesResult>
{
    public async Task<ParseTollPricesResult> Handle(ParseIndianaTollPricesCommand request, CancellationToken ct)
    {
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;

        // Парсим JSON с настройками для snake_case
        List<IndianaTollPriceEntry>? priceEntries;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            priceEntries = JsonSerializer.Deserialize<List<IndianaTollPriceEntry>>(request.JsonContent, options);
            if (priceEntries == null || priceEntries.Count == 0)
            {
                return new ParseTollPricesResult(0, new List<string> { "JSON пуст или невалиден" });
            }
        }
        catch (JsonException ex)
        {
            return new ParseTollPricesResult(0, new List<string> { $"Ошибка парсинга JSON: {ex.Message}" });
        }

        // Получаем или создаем StateCalculator для Indiana
        var indianaCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "IN", ct);

        if (indianaCalculator == null)
        {
            indianaCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "Indiana Toll Road",
                StateCode = "IN"
            };
            _context.StateCalculators.Add(indianaCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // Создаем словарь для кэширования найденных tolls
        var tollCache = new Dictionary<string, List<Toll>>();

        // Собираем цены в батч-словарь (FromId, ToId) -> TollPrice[]
        var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

        // Обрабатываем каждую запись о цене
        foreach (var priceEntry in priceEntries)
        {
            // Находим toll для entry (точка входа)
            var fromTolls = await FindOrCacheTolls(priceEntry.Entry, tollCache, indianaCalculator.Id, ct);
            if (fromTolls.Count == 0)
            {
                notFoundPlazas.Add($"Entry: {priceEntry.Entry}");
                continue;
            }

            // Находим toll для exit (точка выхода)
            var toTolls = await FindOrCacheTolls(priceEntry.Exit, tollCache, indianaCalculator.Id, ct);
            if (toTolls.Count == 0)
            {
                notFoundPlazas.Add($"Exit: {priceEntry.Exit}");
                continue;
            }

            // Парсим цены
            var cashPrice = ParsePrice(priceEntry.CashRate);
            var aviPrice = ParsePrice(priceEntry.AviRate);

            var description = $"{priceEntry.Entry} -> {priceEntry.Exit}";

            // Обрабатываем все комбинации entry -> exit толлов (как в других штатах)
            var pairResults = TollPairProcessor.ProcessAllPairsToDictionaryList(
                fromTolls,
                toTolls,
                (entryToll, exitToll) =>
                {
                    var tollPrices = new List<TollPrice>();

                    if (cashPrice > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.Cash,
                            AxelType = AxelType._5L,
                            Amount = cashPrice,
                            Description = description
                        });
                    }

                    // AVI в источнике используем как EZPass
                    if (aviPrice > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.EZPass,
                            AxelType = AxelType._5L,
                            Amount = aviPrice,
                            Description = $"{description} (AVI)"
                        });
                    }

                    return tollPrices;
                });

            foreach (var kvp in pairResults)
            {
                if (!tollPairsWithPrices.TryGetValue(kvp.Key, out var existingList))
                {
                    existingList = new List<TollPrice>();
                    tollPairsWithPrices[kvp.Key] = existingList;
                }

                existingList.AddRange(kvp.Value);
            }

            updatedCount += pairResults.Count;
        }

        // Батч-создание/обновление CalculatePrice с TollPrice
        if (tollPairsWithPrices.Count > 0)
        {
            var tollPairsWithPricesEnumerable = tollPairsWithPrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPrice>?)kvp.Value);

            await calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                tollPairsWithPricesEnumerable,
                indianaCalculator.Id,
                includeTollPrices: true,
                ct);
        }

        await _context.SaveChangesAsync(ct);

        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Находит Toll'ы по имени/ключу, используя кэш для оптимизации, и устанавливает StateCalculatorId.
    /// Может вернуть несколько toll'ов (как в других штатах), поэтому возвращаем List&lt;Toll&gt;.
    /// </summary>
    private async Task<List<Toll>> FindOrCacheTolls(
        string name,
        Dictionary<string, List<Toll>> cache,
        Guid stateCalculatorId,
        CancellationToken ct)
    {
        var key = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new List<Toll>();
        }

        var keyLower = key.ToLowerInvariant();

        if (cache.TryGetValue(key, out var cachedTolls))
        {
            // Если toll уже найден, но StateCalculatorId не установлен, обновляем его
            foreach (var toll in cachedTolls)
            {
                if (toll.StateCalculatorId != stateCalculatorId)
                {
                    toll.StateCalculatorId = stateCalculatorId;
                }
            }
            return cachedTolls;
        }

        // Ищем точное совпадение по Name/Key (регистронезависимо).
        // Не используем ILIKE, чтобы не зависеть от провайдера/extension-методов.
        var tolls = await _context.Tolls
            .Where(t =>
                (t.Name != null && t.Name.ToLower() == keyLower) ||
                (t.Key != null && t.Key.ToLower() == keyLower) ||
                (t.Number != null && t.Number == key))
            .ToListAsync(ct);

        // Если toll'ы найдены, устанавливаем StateCalculatorId
        if (tolls.Count > 0)
        {
            foreach (var toll in tolls)
            {
                toll.StateCalculatorId = stateCalculatorId;
            }
        }

        cache[key] = tolls;
        return tolls;
    }

    /// <summary>
    /// Парсит цену из строки (убирает символ доллара и пробелы)
    /// </summary>
    private static double ParsePrice(string priceString)
    {
        if (string.IsNullOrWhiteSpace(priceString))
            return 0.0;

        var text = priceString.Trim();
        // Убираем символ доллара и пробелы
        text = text.Replace("$", "").Trim();

        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var price))
        {
            return price;
        }

        return 0.0;
    }
}

