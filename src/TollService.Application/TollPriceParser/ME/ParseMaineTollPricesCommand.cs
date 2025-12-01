using System.Linq;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.ME;

public record ParseMaineTollPricesCommand(string PricesJsonContent)
    : IRequest<ParseMaineTollPricesResult>;

public record ParseMaineTollPricesResult(
    List<string> Lines,
    int UpdatedCount,
    int CreatedCount,
    List<string> NotFoundPlazas,
    List<string> Errors,
    string? Error = null);

public class ParseMaineTollPricesCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseMaineTollPricesCommand, ParseMaineTollPricesResult>
{
    // Maine bounds: approximate (south, west, north, east)
    private static readonly double MeMinLatitude = 43.0;
    private static readonly double MeMinLongitude = -71.0;
    private static readonly double MeMaxLatitude = 45.0;
    private static readonly double MeMaxLongitude = -69.0;

    public async Task<ParseMaineTollPricesResult> Handle(ParseMaineTollPricesCommand request, CancellationToken ct)
    {
        var resultLines = new List<string>();
        var notFoundPlazas = new List<string>();
        var errors = new List<string>();
        int updatedCount = 0;
        int createdCount = 0;

        if (string.IsNullOrWhiteSpace(request.PricesJsonContent))
        {
            return new ParseMaineTollPricesResult(
                new(), 0, 0, new(), new(),
                "PricesJsonContent не может быть пустым");
        }

        MaineTollPricesCollection? data = null;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Сначала пытаемся распарсить полный ответ (как возвращает fetch-maine-prices)
            // {
            //   "prices": { ... MaineTollPricesCollection ... },
            //   "totalCombinations": ...,
            //   "successCount": ...,
            //   "errorCount": ...,
            //   "errors": [...],
            //   "error": null
            // }
            try
            {
                var full = JsonSerializer.Deserialize<MaineFullPricesResponse>(request.PricesJsonContent, options);
                if (full?.Prices != null)
                {
                    data = full.Prices;
                }
            }
            catch (JsonException)
            {
                // Игнорируем, попробуем распарсить как «чистый» MaineTollPricesCollection ниже
            }

            // Если не удалось распарсить как обёртку, пробуем как обычный MaineTollPricesCollection
            if (data == null)
            {
                data = JsonSerializer.Deserialize<MaineTollPricesCollection>(request.PricesJsonContent, options);
            }
        }
        catch (JsonException jsonEx)
        {
            return new ParseMaineTollPricesResult(
                new(), 0, 0, new(), new(),
                $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.Prices == null || data.Prices.Count == 0)
        {
            return new ParseMaineTollPricesResult(
                new(), 0, 0, new(), new(),
                "Цены не найдены в JSON файле");
        }

        // 1. Берём все записи с ценами без ошибок
        var prices = data.Prices;
        var validPrices = prices
            .Where(p => string.IsNullOrWhiteSpace(p.Error) && (p.Cash > 0 || p.EzPass > 0))
            .ToList();

        resultLines.Add($"Всего записей в JSON: {prices.Count}, валидных с ценами: {validPrices.Count}");

        // 2. Получаем или создаем StateCalculator для Maine
        var maineCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "ME", ct);

        if (maineCalculator == null)
        {
            maineCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "Maine Turnpike",
                StateCode = "ME"
            };
            _context.StateCalculators.Add(maineCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // 3. Кэш toll'ов по коду (строковый fromId)
        var tollCacheByCode = new Dictionary<string, List<Toll>>();
        // Кэш toll'ов для toCode (строковый toId)
        var tollToCacheByCode = new Dictionary<string, List<Toll>>();

        foreach (var priceData in validPrices)
        {
            try
            {
                var fromCode = priceData.FromId.ToString(); // "7", "102" и т.п.
                var toCode = priceData.ToId.ToString();

                // ищем / берём из кэша Toll по Name/Key/Number
                if (!tollCacheByCode.TryGetValue(fromCode, out var tollsForCode))
                {
                    tollsForCode = await _context.Tolls
                        .Where(t =>
                            t.Name == fromCode ||
                            t.Key == fromCode ||
                            t.Number == fromCode)
                        .ToListAsync(ct);

                    tollCacheByCode[fromCode] = tollsForCode;
                }

                if (tollsForCode == null || tollsForCode.Count == 0)
                {
                    notFoundPlazas.Add(fromCode);
                    resultLines.Add($"FromCode {fromCode} ({priceData.FromName}): НЕ НАЙДЕНО ни одного Toll");
                    continue;
                }

                // Находим toToll(ы) по коду
                if (!tollToCacheByCode.TryGetValue(toCode, out var tollsToForCode))
                {
                    tollsToForCode = await _context.Tolls
                        .Where(t =>
                            t.Name == toCode ||
                            t.Key == toCode ||
                            t.Number == toCode)
                        .ToListAsync(ct);

                    tollToCacheByCode[toCode] = tollsToForCode;
                }

                if (tollsToForCode == null || tollsToForCode.Count == 0)
                {
                    notFoundPlazas.Add(toCode);
                    resultLines.Add($"ToCode {toCode} ({priceData.ToName}): НЕ НАЙДЕНО ни одного Toll");
                    continue;
                }

                foreach (var fromToll in tollsForCode)
                {
                    // Нормализуем Number для найденных fromToll'ов
                    if (string.IsNullOrWhiteSpace(fromToll.Number))
                    {
                        fromToll.Number = fromCode;
                    }

                    foreach (var toToll in tollsToForCode)
                    {
                        if (fromToll.Id == toToll.Id)
                        {
                            continue;
                        }

                        // 4. Находим или создаем CalculatePrice для пары from->to
                        var calculatePrice = await _context.CalculatePrices.Include(cp => cp.TollPrices)
                            .FirstOrDefaultAsync(cp =>
                                cp.FromId == fromToll.Id &&
                                cp.ToId == toToll.Id &&
                                cp.StateCalculatorId == maineCalculator.Id,
                                ct);

                        if (calculatePrice != null)
                        {
                            // Для Maine CalculatePrice хранит только связь From -> To -> StateCalculator,
                            // сами значения цен храним в TollPrice, поэтому здесь ничего не обновляем.
                            updatedCount++;
                        }
                        else
                        {
                            calculatePrice = new CalculatePrice
                            {
                                Id = Guid.NewGuid(),
                                StateCalculatorId = maineCalculator.Id,
                                FromId = fromToll.Id,
                                ToId = toToll.Id
                            };
                            _context.CalculatePrices.Add(calculatePrice);
                            createdCount++;
                        }

                        var tollPricesToAdd = new List<TollPrice>();

                        // Cash TollPrice, привязанный к CalculatePrice
                        if (priceData.Cash > 0)
                        {
                            var existingCash = await _context.TollPrices
                                .FirstOrDefaultAsync(tp =>
                                    tp.TollId == fromToll.Id &&
                                    tp.PaymentType == TollPaymentType.Cash &&
                                    tp.CalculatePriceId == calculatePrice.Id,
                                    ct);

                            if (existingCash != null)
                            {
                                existingCash.Amount = priceData.Cash;
                                updatedCount++;
                            }
                            else
                            {
                                tollPricesToAdd.Add(new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = fromToll.Id,
                                    CalculatePriceId = calculatePrice.Id,
                                    PaymentType = TollPaymentType.Cash,
                                    Amount = priceData.Cash,
                                    Description = $"To {priceData.ToName}"
                                });
                                createdCount++;
                            }
                        }

                        // EZPass TollPrice
                        if (priceData.EzPass > 0)
                        {
                            var existingEz = await _context.TollPrices
                                .FirstOrDefaultAsync(tp =>
                                    tp.TollId == fromToll.Id &&
                                    tp.PaymentType == TollPaymentType.EZPass &&
                                    tp.CalculatePriceId == calculatePrice.Id,
                                    ct);

                            if (existingEz != null)
                            {
                                existingEz.Amount = priceData.EzPass;
                                updatedCount++;
                            }
                            else
                            {
                                tollPricesToAdd.Add(new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = fromToll.Id,
                                    CalculatePriceId = calculatePrice.Id,
                                    PaymentType = TollPaymentType.EZPass,
                                    Amount = priceData.EzPass,
                                    Description = $"To {priceData.ToName}"
                                });
                                createdCount++;
                            }
                        }

                        if (tollPricesToAdd.Count > 0)
                        {
                            fromToll.TollPrices.AddRange(tollPricesToAdd);
                            _context.TollPrices.AddRange(tollPricesToAdd);
                        }

                        resultLines.Add(
                            $"{priceData.FromName} (code {fromCode}) -> {priceData.ToName} (code {toCode}) - Cash: {priceData.Cash:0.00}, EZPass: {priceData.EzPass:0.00}");
                    }
                }

                // Периодически сохраняем
                if ((updatedCount + createdCount) % 50 == 0)
                {
                    await _context.SaveChangesAsync(ct);
                    resultLines.Add($"Промежуточное сохранение: создано {createdCount}, обновлено {updatedCount}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Ошибка при обработке {priceData.FromId}->{priceData.ToId}: {ex.Message}");
            }
        }

        // Финальное сохранение
        await _context.SaveChangesAsync(ct);

        resultLines.Add($"Обработка завершена. Создано: {createdCount}, Обновлено: {updatedCount}, Не найдено кодов: {notFoundPlazas.Count}, Ошибок: {errors.Count}");

        return new ParseMaineTollPricesResult(
            resultLines,
            updatedCount,
            createdCount,
            notFoundPlazas.Distinct().ToList(),
            errors);
    }
}
