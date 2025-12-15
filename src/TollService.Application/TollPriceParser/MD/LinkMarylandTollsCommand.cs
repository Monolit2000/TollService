using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.MD;

public record MarylandPriceRate(
    string Name,
    List<string> Values);

public record MarylandPriceData(
    List<string> Headers,
    List<MarylandPriceRate> Rates,
    List<string> Savings);

public record MarylandPaymentMethods(
    [property: System.Text.Json.Serialization.JsonPropertyName("tag")] bool Tag,
    [property: System.Text.Json.Serialization.JsonPropertyName("plate")] bool Plate,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] bool Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("card")] bool Card,
    [property: System.Text.Json.Serialization.JsonPropertyName("app")] bool App);

public record LinkMarylandTollsCommand(
    string PricesJson) : IRequest<LinkMarylandTollsResult>;

public record MarylandTollPriceInfo(
    string PaymentType,
    double Amount,
    string? TimePeriod = null);

public record MarylandLinkedTollInfo(
    string Road,
    int TollClass,
    string EntryPlazaValue,
    string EntryPlazaLabel,
    Guid? FromTollId,
    string? FromTollName,
    string? FromTollKey,
    string ExitPlazaValue,
    string ExitPlazaLabel,
    Guid? ToTollId,
    string? ToTollName,
    string? ToTollKey,
    List<MarylandTollPriceInfo> Prices);

public record LinkMarylandTollsResult(
    List<MarylandLinkedTollInfo> LinkedTolls,
    List<string> NotFoundEntryPlazas,
    List<string> NotFoundExitPlazas,
    int UpdatedPairsCount,
    string? Error = null);

// Maryland bounds: (south, west, north, east) = (37.9, -79.5, 39.7, -75.0)
public class LinkMarylandTollsCommandHandler(
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkMarylandTollsCommand, LinkMarylandTollsResult>
{
    private static readonly double MdMinLatitude = 37.9;
    private static readonly double MdMinLongitude = -79.5;
    private static readonly double MdMaxLatitude = 39.7;
    private static readonly double MdMaxLongitude = -75.0;

    // Словарь плаз Maryland: value -> label
    private static readonly Dictionary<string, string> PlazaValueToLabel = new()
    {
        { "i370", "Interstate 370" },
        { "md97", "Georgia Ave (MD 97)" },
        { "md182", "Layhill Rd (MD 182)" },
        { "md650", "New Hampshire Ave (MD 650)" },
        { "us29", "Columbia Pike (US 29) / Briggs Chaney Rd" },
        { "i95", "Interstate 95" },
        { "us1", "Konterra Drive / US 1" },
        { "md43", "MD 43" },
        { "md152", "MD 152" }
    };

    public async Task<LinkMarylandTollsResult> Handle(LinkMarylandTollsCommand request, CancellationToken ct)
    {
        string? link = null;
        PaymentMethod? paymentMethod = null;
        var pricesDict = new Dictionary<string, MarylandPriceData>();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            // Парсим prices.json из строки, извлекая link и payment_methods
            using (var jsonDoc = JsonDocument.Parse(request.PricesJson))
            {
                // Извлекаем свойства, которые являются объектами с ценами, а также link и payment_methods
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                    if (property.Name == "link")
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            link = property.Value.GetString();
                        }
                        continue;
                    }

                    if (property.Name == "payment_methods")
                {
                        var paymentMethods = JsonSerializer.Deserialize<MarylandPaymentMethods>(property.Value.GetRawText(), options);
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
                        continue;
                }

                try
                {
                        var priceData = JsonSerializer.Deserialize<MarylandPriceData>(property.Value.GetRawText(), options);
                    if (priceData != null)
                    {
                        pricesDict[property.Name] = priceData;
                    }
                }
                catch
                {
                    // Игнорируем свойства, которые не являются объектами с ценами
                    }
                }
            }

            if (pricesDict == null || pricesDict.Count == 0)
            {
                return new LinkMarylandTollsResult(
                    new(),
                    new(),
                    new(),
                    0,
                    "Prices не найдены в файле");
            }

            // Получаем или создаем StateCalculator для Maryland
            var marylandCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
                stateCode: "MD",
                calculatorName: "Maryland Toll Facilities",
                ct);

            // Создаем bounding box для Maryland
            var mdBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                MdMinLongitude, MdMinLatitude, MdMaxLongitude, MdMaxLatitude);

            var linkedTolls = new List<MarylandLinkedTollInfo>();
            var notFoundEntryPlazas = new List<string>();
            var notFoundExitPlazas = new List<string>();

            // Словарь для сбора пар с их TollPrice: ключ - (FromId, ToId), значение - список TollPrice
            var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

            // Фильтруем цены: только классы 5 и 6, только дороги ETL и ICC
            var relevantPrices = pricesDict
                .Where(kvp => IsRelevantPriceKey(kvp.Key))
                .ToList();

            // Собираем все уникальные имена плаз для оптимизированного поиска
            var allPlazaLabels = new HashSet<string>();
            var plazaValueToLabelMap = new Dictionary<string, string>();

            foreach (var priceKvp in relevantPrices)
            {
                var priceKey = priceKvp.Key;

                // Парсим ключ: "class5-icc-us1-us29"
                if (!TryParsePriceKey(priceKey, out var tollClass, out var road, out var entryPlazaValue, out var exitPlazaValue))
                {
                    continue;
                }

                // Получаем label для entry и exit плаз
                if (PlazaValueToLabel.TryGetValue(entryPlazaValue.ToLower(), out var entryPlazaLabel))
                {
                    allPlazaLabels.Add(entryPlazaLabel);
                    plazaValueToLabelMap[entryPlazaValue.ToLower()] = entryPlazaLabel;
                }

                if (PlazaValueToLabel.TryGetValue(exitPlazaValue.ToLower(), out var exitPlazaLabel))
                {
                    allPlazaLabels.Add(exitPlazaLabel);
                    plazaValueToLabelMap[exitPlazaValue.ToLower()] = exitPlazaLabel;
                }
            }

            // Загружаем все tolls одним запросом к БД
            var tollsByLabel = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
                allPlazaLabels,
                mdBoundingBox,
                TollSearchOptions.NameOrKey,
                websiteUrl: link,
                paymentMethod: paymentMethod,
                ct);

            // Обрабатываем цены
            foreach (var priceKvp in relevantPrices)
            {
                var priceKey = priceKvp.Key;
                var priceData = priceKvp.Value;

                // Парсим ключ: "class5-icc-us1-us29"
                if (!TryParsePriceKey(priceKey, out var tollClass, out var road, out var entryPlazaValue, out var exitPlazaValue))
                {
                    continue;
                }

                // Получаем label для entry и exit плаз
                if (!plazaValueToLabelMap.TryGetValue(entryPlazaValue.ToLower(), out var entryPlazaLabel))
                {
                    notFoundEntryPlazas.Add($"{entryPlazaValue} (для ключа {priceKey})");
                    continue;
                }

                if (!plazaValueToLabelMap.TryGetValue(exitPlazaValue.ToLower(), out var exitPlazaLabel))
                {
                    notFoundExitPlazas.Add($"{exitPlazaValue} (для ключа {priceKey})");
                    continue;
                }

                // Получаем найденные tolls из кэша
                if (!tollsByLabel.TryGetValue(entryPlazaLabel, out var entryTolls) || entryTolls.Count == 0)
                {
                    notFoundEntryPlazas.Add($"{entryPlazaLabel} ({entryPlazaValue})");
                    continue;
                }

                if (!tollsByLabel.TryGetValue(exitPlazaLabel, out var exitTolls) || exitTolls.Count == 0)
                {
                    notFoundExitPlazas.Add($"{exitPlazaLabel} ({exitPlazaValue})");
                    continue;
                }

                // Устанавливаем Number и StateCalculatorId для найденных tolls
                _tollNumberService.SetNumberAndCalculatorId(entryTolls, entryPlazaValue, marylandCalculator.Id, updateNumberIfDifferent: true);
                _tollNumberService.SetNumberAndCalculatorId(exitTolls, exitPlazaValue, marylandCalculator.Id, updateNumberIfDifferent: true);

                // Определяем AxelType
                var axelType = tollClass switch
                {
                    5 => AxelType._5L,
                    6 => AxelType._6L,
                    _ => AxelType._5L
                };

                // Обрабатываем все комбинации entry -> exit толлов
                foreach (var entryToll in entryTolls)
                {
                    foreach (var exitToll in exitTolls)
                    {
                        if (entryToll.Id == exitToll.Id)
                        {
                            continue;
                        }

                        var pairKey = (entryToll.Id, exitToll.Id);
                        if (!tollPairsWithPrices.ContainsKey(pairKey))
                        {
                            tollPairsWithPrices[pairKey] = new List<TollPrice>();
                        }

                        // Парсим цены из priceData для вывода в response и создания TollPrice
                        var prices = new List<MarylandTollPriceInfo>();
                        var description = $"Maryland {road.ToUpper()} - {entryPlazaLabel} -> {exitPlazaLabel} (Class {tollClass})";

                        // Обрабатываем rates
                        foreach (var rate in priceData.Rates)
                        {
                            var paymentType = ParsePaymentType(rate.Name);
                            if (paymentType == null)
                            {
                                continue;
                            }

                            // Обрабатываем values (может быть один или несколько для Peak/Off-Peak/Overnight)
                            for (int i = 0; i < rate.Values.Count; i++)
                            {
                                var valueStr = rate.Values[i];
                                // Убираем звездочки и другие символы
                                valueStr = Regex.Replace(valueStr, @"[^\d.]", "");

                                if (!double.TryParse(valueStr, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                                {
                                    continue;
                                }

                                // Определяем период времени
                                var timePeriod = i < priceData.Headers.Count ? priceData.Headers[i] : null;

                                // Добавляем в response
                                prices.Add(new MarylandTollPriceInfo(
                                    PaymentType: rate.Name,
                                    Amount: amount,
                                    TimePeriod: timePeriod));

                                // Создаем TollPrice для CalculatePrice (24/7 - без привязки ко времени суток)
                                var tollPrice = new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = entryToll.Id,
                                    PaymentType = paymentType.Value,
                                    AxelType = axelType,
                                    Amount = amount,
                                    Description = $"{description} - {rate.Name}" + (!string.IsNullOrWhiteSpace(timePeriod) ? $" ({timePeriod})" : "")
                                };

                                tollPairsWithPrices[pairKey].Add(tollPrice);
                            }
                        }

                        // Добавляем информацию о связанном toll с ценами
                        linkedTolls.Add(new MarylandLinkedTollInfo(
                            Road: road.ToUpper(),
                            TollClass: tollClass,
                            EntryPlazaValue: entryPlazaValue,
                            EntryPlazaLabel: entryPlazaLabel,
                            FromTollId: entryToll.Id,
                            FromTollName: entryToll.Name,
                            FromTollKey: entryToll.Key,
                            ExitPlazaValue: exitPlazaValue,
                            ExitPlazaLabel: exitPlazaLabel,
                            ToTollId: exitToll.Id,
                            ToTollName: exitToll.Name,
                            ToTollKey: exitToll.Key,
                            Prices: prices));
                    }
                }
            }

            // Батч-создание/обновление CalculatePrice с TollPrice
            int updatedPairsCount = 0;
            if (tollPairsWithPrices.Count > 0)
            {
                var tollPairsWithPricesEnumerable = tollPairsWithPrices.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IEnumerable<TollPrice>?)kvp.Value);

                var result = await _calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                    tollPairsWithPricesEnumerable,
                    marylandCalculator.Id,
                    includeTollPrices: true,
                    ct);

                updatedPairsCount = result.Count;
            }

            // Сохраняем все изменения
            await _context.SaveChangesAsync(ct);

            return new LinkMarylandTollsResult(
                linkedTolls,
                notFoundEntryPlazas.Distinct().ToList(),
                notFoundExitPlazas.Distinct().ToList(),
                updatedPairsCount);
        }
        catch (Exception ex)
        {
            return new LinkMarylandTollsResult(
                new(),
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }

    private static bool IsRelevantPriceKey(string key)
    {
        // Проверяем, что ключ начинается с class5 или class6
        if (!key.StartsWith("class5-", StringComparison.OrdinalIgnoreCase) &&
            !key.StartsWith("class6-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Проверяем, что содержит -etl- или -icc-
        return key.Contains("-etl-", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("-icc-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParsePriceKey(
        string key,
        out int tollClass,
        out string road,
        out string entryPlazaValue,
        out string exitPlazaValue)
    {
        tollClass = 0;
        road = string.Empty;
        entryPlazaValue = string.Empty;
        exitPlazaValue = string.Empty;

        // Формат: "class5-icc-us1-us29"
        var parts = key.Split('-');
        if (parts.Length < 4)
        {
            return false;
        }

        // Парсим класс
        if (!int.TryParse(parts[0].Replace("class", "", StringComparison.OrdinalIgnoreCase), out tollClass))
        {
            return false;
        }

        // Парсим дорогу
        road = parts[1];

        // Парсим entry и exit
        entryPlazaValue = parts[2];
        exitPlazaValue = parts[3];

        return true;
    }

    private static TollPaymentType? ParsePaymentType(string name)
    {
        var nameLower = name.ToLower();
        if (nameLower.Contains("e-zpass maryland") || nameLower.Contains("ezpass maryland"))
        {
            return TollPaymentType.EZPass;
        }
        else if (nameLower.Contains("out of state e-zpass") || nameLower.Contains("out of state ezpass"))
        {
            return TollPaymentType.OutOfStateEZPass;
        }
        else if (nameLower.Contains("pay-by-plate") || nameLower.Contains("pay by plate"))
        {
            return TollPaymentType.PayOnline;
        }
        else if (nameLower.Contains("video tolls") || nameLower.Contains("video"))
        {
            return TollPaymentType.VideoTolls;
        }

        return null;
    }


}

