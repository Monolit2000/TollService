using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser;

public record ParseIndianaTollPricesCommand(string JsonContent) 
    : IRequest<ParseTollPricesResult>;

public record IndianaTollPriceEntry(
    [property: JsonPropertyName("entry")] string Entry,
    [property: JsonPropertyName("exit")] string Exit,
    [property: JsonPropertyName("cash_rate")] string CashRate,
    [property: JsonPropertyName("avi_rate")] string AviRate);

public class ParseIndianaTollPricesCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseIndianaTollPricesCommand, ParseTollPricesResult>
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
        var tollCache = new Dictionary<string, Toll?>();

        // Обрабатываем каждую запись о цене
        foreach (var priceEntry in priceEntries)
        {
            // Находим toll для entry (точка входа)
            var fromToll = await FindOrCacheToll(priceEntry.Entry, tollCache, indianaCalculator.Id, ct);
            if (fromToll == null)
            {
                notFoundPlazas.Add($"Entry: {priceEntry.Entry}");
                continue;
            }

            // Находим toll для exit (точка выхода)
            var toToll = await FindOrCacheToll(priceEntry.Exit, tollCache, indianaCalculator.Id, ct);
            if (toToll == null)
            {
                notFoundPlazas.Add($"Exit: {priceEntry.Exit}");
                continue;
            }

            // Парсим цены
            var cashPrice = ParsePrice(priceEntry.CashRate);
            var aviPrice = ParsePrice(priceEntry.AviRate);

            // Проверяем, существует ли уже CalculatePrice для этой пары
            var existingPrice = await _context.CalculatePrices
                .FirstOrDefaultAsync(cp => 
                    cp.FromId == fromToll.Id && 
                    cp.ToId == toToll.Id && 
                    cp.StateCalculatorId == indianaCalculator.Id, ct);

            if (existingPrice != null)
            {
                // Обновляем существующую запись
                existingPrice.Cash = cashPrice;
                existingPrice.IPass = aviPrice; // AVI rate соответствует IPass/EZPass
                existingPrice.Online = aviPrice; // Online также использует AVI rate
            }
            else
            {
                // Создаем новую запись
                var calculatePrice = new CalculatePrice
                {
                    Id = Guid.NewGuid(),
                    StateCalculatorId = indianaCalculator.Id,
                    FromId = fromToll.Id,
                    ToId = toToll.Id,
                    Cash = cashPrice,
                    IPass = aviPrice,
                    Online = aviPrice
                };
                _context.CalculatePrices.Add(calculatePrice);
            }

            updatedCount++;
        }

        await _context.SaveChangesAsync(ct);

        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Находит Toll по имени, используя кэш для оптимизации, и устанавливает StateCalculatorId
    /// </summary>
    private async Task<Toll?> FindOrCacheToll(string name, Dictionary<string, Toll?> cache, Guid stateCalculatorId, CancellationToken ct)
    {
        if (cache.TryGetValue(name, out var cachedToll))
        {
            // Если toll уже найден, но StateCalculatorId не установлен, обновляем его
            if (cachedToll != null && cachedToll.StateCalculatorId != stateCalculatorId)
            {
                cachedToll.StateCalculatorId = stateCalculatorId;
            }
            return cachedToll;
        }

        // Ищем точное совпадение по имени (регистронезависимо)
        var toll = await _context.Tolls
            .FirstOrDefaultAsync(t => t.Name != null && t.Name == name, ct);

        //// Если не найдено, пробуем поиск по Key
        //if (toll == null)
        //{
        //    toll = await _context.Tolls
        //        .FirstOrDefaultAsync(t => t.Key != null && EF.Functions.ILike(t.Key, name), ct);
        //}

        //// Если не найдено, пробуем частичное совпадение (содержит) используя ILike для PostgreSQL
        //if (toll == null)
        //{
        //    toll = await _context.Tolls
        //        .FirstOrDefaultAsync(t => (t.Name != null && EF.Functions.ILike(t.Name, $"%{name}%")) ||
        //                                 (t.Key != null && EF.Functions.ILike(t.Key, $"%{name}%")), ct);
        //}

        // Если toll найден, устанавливаем StateCalculatorId
        if (toll != null)
        {
            toll.StateCalculatorId = stateCalculatorId;
        }

        cache[name] = toll;
        return toll;
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

