using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.TX;

// Формат 1: toll_locations с Toll_Plaza и rates (TollTag, ZipCash)
public record TexasTollLocationV1(
    [property: JsonPropertyName("Toll_Plaza")] string TollPlaza,
    [property: JsonPropertyName("rates")] TexasRatesV1? Rates);

public record TexasRatesV1(
    [property: JsonPropertyName("5_Axle")] TexasAxleRateV1? Axles5,
    [property: JsonPropertyName("6_Axle")] TexasAxleRateV1? Axles6,
    [property: JsonPropertyName("5_Axle_and_above")] TexasAxleRateV1? Axles5AndAbove,
    [property: JsonPropertyName("6_Axle_and_above")] TexasAxleRateV1? Axles6AndAbove);

public record TexasAxleRateV1(
    [property: JsonPropertyName("TollTag")] double? TollTag,
    [property: JsonPropertyName("ZipCash")] double? ZipCash);

// Формат 2: toll_locations с name и 5 Axle/6 Axle (TxTag, Pay by Mail)
public record TexasTollLocationV2(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("5 Axle")] TexasAxleRateV2? Axles5,
    [property: JsonPropertyName("6 Axle")] TexasAxleRateV2? Axles6);

public record TexasAxleRateV2(
    [property: JsonPropertyName("TxTag")] string? TxTag,
    [property: JsonPropertyName("Pay by Mail")] string? PayByMail);

// Формат 3: toll_rates с standard_toll_rates
public record TexasTollRatesV3(
    [property: JsonPropertyName("Toll_Plaza")] string TollPlaza,
    [property: JsonPropertyName("2-Axle")] double? Axle2,
    [property: JsonPropertyName("3-Axle")] double? Axle3,
    [property: JsonPropertyName("4-Axle")] double? Axle4,
    [property: JsonPropertyName("5-Axle")] double? Axle5,
    [property: JsonPropertyName("6-Axle")] double? Axle6);

// Модели для JSON структур
public record TexasPricesDataV1(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV1>? TollLocations);

public record TexasPricesDataV2(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV2>? TollLocations);

public record TexasPricesDataV3(
    [property: JsonPropertyName("toll_rates")] TexasTollRatesContainer? TollRates);

public record TexasTollRatesContainer(
    [property: JsonPropertyName("standard_toll_rates")] List<TexasTollRatesV3>? StandardTollRates);

public record TexasTollPriceInfo(
    string PaymentType,
    double Amount,
    int Axles);

public record TexasLinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    List<TexasTollPriceInfo> Prices);

public record ParseTexasTollPricesCommand(
    string JsonPayload) : IRequest<ParseTexasTollPricesResult>;

public record ParseTexasTollPricesResult(
    List<TexasLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// Texas bounds: (south, west, north, east) = (25.8, -106.6, 36.5, -93.5)
public class ParseTexasTollPricesCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseTexasTollPricesCommand, ParseTexasTollPricesResult>
{
    private static readonly double TxMinLatitude = 25.8;
    private static readonly double TxMinLongitude = -106.6;
    private static readonly double TxMaxLatitude = 36.5;
    private static readonly double TxMaxLongitude = -93.5;

    public async Task<ParseTexasTollPricesResult> Handle(ParseTexasTollPricesCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new ParseTexasTollPricesResult(
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            JsonDocument? jsonDoc;
            try
            {
                jsonDoc = JsonDocument.Parse(request.JsonPayload);
            }
            catch (JsonException jsonEx)
            {
                return new ParseTexasTollPricesResult(
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            // Определяем формат данных
            var format = DetectFormat(jsonDoc.RootElement);
            
            // Создаем bounding box для Texas
            var txBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                TxMinLongitude, TxMinLatitude, TxMaxLongitude, TxMaxLatitude);

            var linkedTolls = new List<TexasLinkedTollInfo>();
            var notFoundPlazas = new List<string>();
            var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

            switch (format)
            {
                case TexasJsonFormat.Format1:
                    await ProcessFormat1(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
                    break;
                case TexasJsonFormat.Format2:
                    await ProcessFormat2(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
                    break;
                case TexasJsonFormat.Format3:
                    await ProcessFormat3(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
                    break;
                default:
                    return new ParseTexasTollPricesResult(
                        new(),
                        new(),
                        0,
                        "Не удалось определить формат JSON");
            }

            // Батч-установка цен
            int updatedTollsCount = 0;
            if (tollsToUpdatePrices.Count > 0)
            {
                var tollsToUpdatePricesEnumerable = tollsToUpdatePrices.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IEnumerable<TollPriceData>)kvp.Value);

                var updatedPricesResult = await _calculatePriceService.SetTollPricesDirectlyBatchAsync(
                    tollsToUpdatePricesEnumerable,
                    ct);
                updatedTollsCount = updatedPricesResult.Count;
            }

            // Сохраняем все изменения
            await _context.SaveChangesAsync(ct);

            return new ParseTexasTollPricesResult(
                linkedTolls,
                notFoundPlazas.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new ParseTexasTollPricesResult(
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }

    private enum TexasJsonFormat
    {
        Unknown,
        Format1, // toll_locations с Toll_Plaza и rates
        Format2, // toll_locations с name и 5 Axle/6 Axle
        Format3  // toll_rates с standard_toll_rates
    }

    private static TexasJsonFormat DetectFormat(JsonElement root)
    {
        // Проверяем формат 3: наличие toll_rates
        if (root.TryGetProperty("toll_rates", out _))
        {
            return TexasJsonFormat.Format3;
        }

        // Проверяем формат 1 или 2: наличие toll_locations
        if (root.TryGetProperty("toll_locations", out var tollLocations) && tollLocations.ValueKind == JsonValueKind.Array)
        {
            if (tollLocations.GetArrayLength() > 0)
            {
                var firstItem = tollLocations[0];
                
                // Формат 1: есть Toll_Plaza
                if (firstItem.TryGetProperty("Toll_Plaza", out _))
                {
                    return TexasJsonFormat.Format1;
                }
                
                // Формат 2: есть name
                if (firstItem.TryGetProperty("name", out _))
                {
                    return TexasJsonFormat.Format2;
                }
            }
        }

        return TexasJsonFormat.Unknown;
    }

    private async Task ProcessFormat1(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV1>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.TollLocations == null || data.TollLocations.Count == 0)
        {
            return;
        }

        // Собираем все уникальные имена плаз
        var allPlazaNames = data.TollLocations
            .Where(p => !string.IsNullOrWhiteSpace(p.TollPlaza))
            .Select(p => p.TollPlaza)
            .Distinct()
            .ToList();

        if (allPlazaNames.Count == 0)
        {
            return;
        }

        // Оптимизированный поиск tolls
        var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allPlazaNames,
            boundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        foreach (var plaza in data.TollLocations)
        {
            if (string.IsNullOrWhiteSpace(plaza.TollPlaza))
                continue;

            if (!tollsByPlazaName.TryGetValue(plaza.TollPlaza.ToLower(), out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.TollPlaza);
                continue;
            }

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                // Обрабатываем 5 осей
                var axles5 = plaza.Rates?.Axles5 ?? plaza.Rates?.Axles5AndAbove;
                if (axles5 != null)
                {
                    if (axles5.TollTag.HasValue && axles5.TollTag.Value > 0)
                    {
                        var amount = axles5.TollTag.Value;
                        prices.Add(new TexasTollPriceInfo("TollTag", amount, 5));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.EZPass,
                            AxelType: AxelType._5L,
                            Description: $"Texas {plaza.TollPlaza} - TollTag (5 axles)"));
                    }

                    if (axles5.ZipCash.HasValue && axles5.ZipCash.Value > 0)
                    {
                        var amount = axles5.ZipCash.Value;
                        prices.Add(new TexasTollPriceInfo("ZipCash", amount, 5));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.Cash,
                            AxelType: AxelType._5L,
                            Description: $"Texas {plaza.TollPlaza} - ZipCash (5 axles)"));
                    }
                }

                // Обрабатываем 6 осей
                var axles6 = plaza.Rates?.Axles6 ?? plaza.Rates?.Axles6AndAbove;
                if (axles6 != null)
                {
                    if (axles6.TollTag.HasValue && axles6.TollTag.Value > 0)
                    {
                        var amount = axles6.TollTag.Value;
                        prices.Add(new TexasTollPriceInfo("TollTag", amount, 6));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.EZPass,
                            AxelType: AxelType._6L,
                            Description: $"Texas {plaza.TollPlaza} - TollTag (6 axles)"));
                    }

                    if (axles6.ZipCash.HasValue && axles6.ZipCash.Value > 0)
                    {
                        var amount = axles6.ZipCash.Value;
                        prices.Add(new TexasTollPriceInfo("ZipCash", amount, 6));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.Cash,
                            AxelType: AxelType._6L,
                            Description: $"Texas {plaza.TollPlaza} - ZipCash (6 axles)"));
                    }
                }

                if (prices.Count > 0)
                {
                    linkedTolls.Add(new TexasLinkedTollInfo(
                        PlazaName: plaza.TollPlaza,
                        TollId: toll.Id,
                        TollName: toll.Name,
                        TollKey: toll.Key,
                        Prices: prices));
                }
            }
        }
    }

    private async Task ProcessFormat2(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV2>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.TollLocations == null || data.TollLocations.Count == 0)
        {
            return;
        }

        // Собираем все уникальные имена плаз
        var allPlazaNames = data.TollLocations
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name)
            .Distinct()
            .ToList();

        if (allPlazaNames.Count == 0)
        {
            return;
        }

        // Оптимизированный поиск tolls
        var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allPlazaNames,
            boundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        foreach (var plaza in data.TollLocations)
        {
            if (string.IsNullOrWhiteSpace(plaza.Name))
                continue;

            if (!tollsByPlazaName.TryGetValue(plaza.Name.ToLower(), out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.Name);
                continue;
            }

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                // Обрабатываем 5 осей
                if (plaza.Axles5 != null)
                {
                    if (!string.IsNullOrWhiteSpace(plaza.Axles5.TxTag))
                    {
                        var amount = ParsePrice(plaza.Axles5.TxTag);
                        if (amount > 0)
                        {
                            prices.Add(new TexasTollPriceInfo("TxTag", amount, 5));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: TollPaymentType.EZPass,
                                AxelType: AxelType._5L,
                                Description: $"Texas {plaza.Name} - TxTag (5 axles)"));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(plaza.Axles5.PayByMail))
                    {
                        var amount = ParsePrice(plaza.Axles5.PayByMail);
                        if (amount > 0)
                        {
                            prices.Add(new TexasTollPriceInfo("Pay by Mail", amount, 5));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: TollPaymentType.PayOnline,
                                AxelType: AxelType._5L,
                                Description: $"Texas {plaza.Name} - Pay by Mail (5 axles)"));
                        }
                    }
                }

                // Обрабатываем 6 осей
                if (plaza.Axles6 != null)
                {
                    if (!string.IsNullOrWhiteSpace(plaza.Axles6.TxTag))
                    {
                        var amount = ParsePrice(plaza.Axles6.TxTag);
                        if (amount > 0)
                        {
                            prices.Add(new TexasTollPriceInfo("TxTag", amount, 6));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: TollPaymentType.EZPass,
                                AxelType: AxelType._6L,
                                Description: $"Texas {plaza.Name} - TxTag (6 axles)"));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(plaza.Axles6.PayByMail))
                    {
                        var amount = ParsePrice(plaza.Axles6.PayByMail);
                        if (amount > 0)
                        {
                            prices.Add(new TexasTollPriceInfo("Pay by Mail", amount, 6));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: TollPaymentType.PayOnline,
                                AxelType: AxelType._6L,
                                Description: $"Texas {plaza.Name} - Pay by Mail (6 axles)"));
                        }
                    }
                }

                if (prices.Count > 0)
                {
                    linkedTolls.Add(new TexasLinkedTollInfo(
                        PlazaName: plaza.Name,
                        TollId: toll.Id,
                        TollName: toll.Name,
                        TollKey: toll.Key,
                        Prices: prices));
                }
            }
        }
    }

    private async Task ProcessFormat3(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV3>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.TollRates?.StandardTollRates == null || data.TollRates.StandardTollRates.Count == 0)
        {
            return;
        }

        // Собираем все уникальные имена плаз
        var allPlazaNames = data.TollRates.StandardTollRates
            .Where(p => !string.IsNullOrWhiteSpace(p.TollPlaza))
            .Select(p => p.TollPlaza)
            .Distinct()
            .ToList();

        if (allPlazaNames.Count == 0)
        {
            return;
        }

        // Оптимизированный поиск tolls
        var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allPlazaNames,
            boundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        foreach (var plaza in data.TollRates.StandardTollRates)
        {
            if (string.IsNullOrWhiteSpace(plaza.TollPlaza))
                continue;

            if (!tollsByPlazaName.TryGetValue(plaza.TollPlaza.ToLower(), out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.TollPlaza);
                continue;
            }

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                // Обрабатываем 5 осей
                if (plaza.Axle5.HasValue && plaza.Axle5.Value > 0)
                {
                    var amount = plaza.Axle5.Value;
                    prices.Add(new TexasTollPriceInfo("Standard", amount, 5));

                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.Cash,
                        AxelType: AxelType._5L,
                        Description: $"Texas {plaza.TollPlaza} - Standard Rate (5 axles)"));
                }

                // Обрабатываем 6 осей
                if (plaza.Axle6.HasValue && plaza.Axle6.Value > 0)
                {
                    var amount = plaza.Axle6.Value;
                    prices.Add(new TexasTollPriceInfo("Standard", amount, 6));

                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.Cash,
                        AxelType: AxelType._6L,
                        Description: $"Texas {plaza.TollPlaza} - Standard Rate (6 axles)"));
                }

                if (prices.Count > 0)
                {
                    linkedTolls.Add(new TexasLinkedTollInfo(
                        PlazaName: plaza.TollPlaza,
                        TollId: toll.Id,
                        TollName: toll.Name,
                        TollKey: toll.Key,
                        Prices: prices));
                }
            }
        }
    }

    private static double ParsePrice(string? priceString)
    {
        if (string.IsNullOrWhiteSpace(priceString))
            return 0;

        // Убираем символ доллара и пробелы
        var cleaned = priceString.Replace("$", "").Trim();

        // Убираем все нецифровые символы кроме точки
        cleaned = Regex.Replace(cleaned, @"[^\d.]", "");

        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var price))
        {
            return price;
        }

        return 0;
    }
}

