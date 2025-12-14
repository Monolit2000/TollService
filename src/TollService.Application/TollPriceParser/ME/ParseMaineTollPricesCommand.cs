using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
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

// Maine bounds: approximate (south, west, north, east)
public class ParseMaineTollPricesCommandHandler(
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseMaineTollPricesCommand, ParseMaineTollPricesResult>
{
    private static readonly double MeMinLatitude = 43.0;
    private static readonly double MeMinLongitude = -71.0;
    private static readonly double MeMaxLatitude = 45.0;
    private static readonly double MeMaxLongitude = -69.0;

    public async Task<ParseMaineTollPricesResult> Handle(ParseMaineTollPricesCommand request, CancellationToken ct)
    {
        var resultLines = new List<string>();
        var notFoundPlazas = new List<string>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.PricesJsonContent))
        {
            return new ParseMaineTollPricesResult(
                new(), 0, 0, new(), new(),
                "PricesJsonContent не может быть пустым");
        }

        MaineTollPricesCollection? data = null;
        string? link = null;
        PaymentMethod? paymentMethod = null;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            // Используем JsonDocument для обработки полей link и payment_methods
            using (JsonDocument doc = JsonDocument.Parse(request.PricesJsonContent))
            {
                // Сначала пытаемся распарсить полный ответ (как возвращает fetch-maine-prices)
                // {
                //   "prices": { ... MaineTollPricesCollection ... },
                //   "totalCombinations": ...,
                //   "successCount": ...,
                //   "errorCount": ...,
                //   "errors": [...],
                //   "error": null,
                //   "link": "...",
                //   "payment_methods": {...}
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

                // Читаем link
                if (doc.RootElement.TryGetProperty("link", out var linkElement) && linkElement.ValueKind == JsonValueKind.String)
                {
                    link = linkElement.GetString();
                }

                // Читаем payment_methods
                if (doc.RootElement.TryGetProperty("payment_methods", out var paymentMethodsElement))
                {
                    var paymentMethods = JsonSerializer.Deserialize<MainePaymentMethods>(paymentMethodsElement.GetRawText(), options);
                    if (paymentMethods != null)
                    {
                        // Маппинг: plate -> NoPlate (обратная логика), card -> NoCard (обратная логика)
                        paymentMethod = new PaymentMethod(
                            tag: paymentMethods.Tag,
                            noPlate: !paymentMethods.Plate,
                            cash: paymentMethods.Cash,
                            noCard: !paymentMethods.Card,
                            app: paymentMethods.App);
                    }
                }
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

        // Получаем или создаем StateCalculator для Maine
        var maineCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
            stateCode: "ME",
            calculatorName: "Maine Turnpike",
            ct);

        // Создаем bounding box для Maine
        var meBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            MeMinLongitude,
            MeMinLatitude,
            MeMaxLongitude,
            MeMaxLatitude);

        // Собираем все уникальные коды из всех записей для батч-поиска
        var allCodes = new HashSet<string>();
        foreach (var priceData in validPrices)
        {
            allCodes.Add(priceData.FromId.ToString());
            allCodes.Add(priceData.ToId.ToString());
        }

        // Один батч-запрос для поиска всех толлов по кодам
        var tollsByCode = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allCodes,
            meBoundingBox,
            TollSearchOptions.NameOrKey,
            websiteUrl: link,
            paymentMethod: paymentMethod,
            ct);

        // Фильтруем результаты: оставляем только точные совпадения по Key, Name или Number
        var exactMatchesByCode = new Dictionary<string, List<Toll>>();
        foreach (var kvp in tollsByCode)
        {
            var searchCode = kvp.Key;
            var tollsForKey = kvp.Value;

            var exactMatches = tollsForKey
                .Where(t =>
                    (t.Key != null && (t.Key == searchCode || t.Key.Equals(searchCode, StringComparison.OrdinalIgnoreCase))) ||
                    (t.Name != null && (t.Name == searchCode || t.Name.Equals(searchCode, StringComparison.OrdinalIgnoreCase))) ||
                    (t.Number != null && t.Number == searchCode))
                .ToList();

            if (exactMatches.Count > 0)
            {
                exactMatchesByCode[searchCode] = exactMatches;
            }
        }

        var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

        foreach (var priceData in validPrices)
        {
            try
            {
                var fromCode = priceData.FromId.ToString(); // "7", "102" и т.п.
                var toCode = priceData.ToId.ToString();

                // Получаем entry tolls из кэша
                if (!exactMatchesByCode.TryGetValue(fromCode, out var filteredEntryTolls) || filteredEntryTolls.Count == 0)
                {
                    notFoundPlazas.Add($"FromCode: {fromCode} ({priceData.FromName})");
                    resultLines.Add($"FromCode {fromCode} ({priceData.FromName}): НЕ НАЙДЕНО");
                    continue;
                }

                // Получаем exit tolls из кэша
                if (!exactMatchesByCode.TryGetValue(toCode, out var filteredExitTolls) || filteredExitTolls.Count == 0)
                {
                    notFoundPlazas.Add($"ToCode: {toCode} ({priceData.ToName})");
                    resultLines.Add($"ToCode {toCode} ({priceData.ToName}): НЕ НАЙДЕНО");
                    continue;
                }

                // Если нашли оба toll, обрабатываем сразу
                if (filteredEntryTolls.Count > 0 && filteredExitTolls.Count > 0)
                {
                    // Устанавливаем Number и StateCalculatorId для найденных tolls
                    _tollNumberService.SetNumberAndCalculatorId(
                        filteredEntryTolls,
                        fromCode,
                        maineCalculator.Id,
                        updateNumberIfDifferent: true);

                    _tollNumberService.SetNumberAndCalculatorId(
                        filteredExitTolls,
                        toCode,
                        maineCalculator.Id,
                        updateNumberIfDifferent: true);

                    var description = $"{priceData.FromName} -> {priceData.ToName}";

                    // Обрабатываем все комбинации entry -> exit толлов
                    var pairResults = TollPairProcessor.ProcessAllPairsToDictionaryList(
                        filteredEntryTolls,
                        filteredExitTolls,
                        (entryToll, exitToll) =>
                        {
                            var tollPrices = new List<TollPrice>();

                            // Создаем TollPrice для Cash
                            if (priceData.Cash > 0)
                            {
                                tollPrices.Add(new TollPrice
                                {
                                    TollId = entryToll.Id,
                                    PaymentType = TollPaymentType.Cash,
                                    AxelType = AxelType._5L,
                                    Amount = priceData.Cash,
                                    Description = description
                                });
                            }

                            // Создаем TollPrice для EZPass
                            if (priceData.EzPass > 0)
                            {
                                tollPrices.Add(new TollPrice
                                {
                                    TollId = entryToll.Id,
                                    PaymentType = TollPaymentType.EZPass,
                                    AxelType = AxelType._5L,
                                    Amount = priceData.EzPass,
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
                    resultLines.Add(
                        $"{priceData.FromName} (code {fromCode}) -> {priceData.ToName} (code {toCode}) - Cash: {priceData.Cash:0.00}, EZPass: {priceData.EzPass:0.00}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Ошибка при обработке {priceData.FromId}->{priceData.ToId}: {ex.Message}");
            }
        }

        // Батч-создание/обновление CalculatePrice с TollPrice
        if (tollPairsWithPrices.Count > 0)
        {
            var tollPairsWithPricesEnumerable = tollPairsWithPrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPrice>?)kvp.Value);

            await _calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                tollPairsWithPricesEnumerable,
                maineCalculator.Id,
                includeTollPrices: true,
                ct);
        }

        // Сохраняем все изменения (Toll, CalculatePrice, TollPrice)
        await _context.SaveChangesAsync(ct);

        resultLines.Add($"Обработка завершена. Не найдено кодов: {notFoundPlazas.Count}, Ошибок: {errors.Count}");

        return new ParseMaineTollPricesResult(
            resultLines,
            0,
            0,
            notFoundPlazas.Distinct().ToList(),
            errors);
    }
}
