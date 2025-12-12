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

// Формат 7: toll_locations с Toll_Gantry_Location и rates (Transponder_Rate, Pay_by_Mail_Rate)
public record TexasTollLocationV7(
    [property: JsonPropertyName("Toll_Gantry_Location")] string TollGantryLocation,
    [property: JsonPropertyName("rates")] TexasRatesV7? Rates);

public record TexasRatesV7(
    [property: JsonPropertyName("5_Axles")] TexasAxleRateV7? Axles5,
    [property: JsonPropertyName("6_Axles_and_above")] TexasAxleRateV7? Axles6AndAbove);

public record TexasAxleRateV7(
    [property: JsonPropertyName("Transponder_Rate")] double? TransponderRate,
    [property: JsonPropertyName("Pay_by_Mail_Rate")] double? PayByMailRate);

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

// Формат 4: toll_locations с name и rates (2_axles_tag, 3_axles_tag, и т.д.)
public record TexasTollLocationV4(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("rates")] TexasRatesV4? Rates);

// Формат 5: toll_locations с location_name и rates_by_axle (массив)
public record TexasTollLocationV5(
    [property: JsonPropertyName("location_name")] string LocationName,
    [property: JsonPropertyName("rates_by_axle")] List<TexasAxleRateV5>? RatesByAxle);

public record TexasAxleRateV5(
    [property: JsonPropertyName("axle_count")] int AxleCount,
    [property: JsonPropertyName("Tag*")] double? Tag,
    [property: JsonPropertyName("Non-Tag†")] double? NonTag);

// Формат 6: toll_locations с location_name, schedule_type и rates (с time_range)
public record TexasTollLocationV6(
    [property: JsonPropertyName("location_id")] int? LocationId,
    [property: JsonPropertyName("location_name")] string LocationName,
    [property: JsonPropertyName("schedule_type")] string? ScheduleType,
    [property: JsonPropertyName("rates")] List<TexasRateV6>? Rates);

public record TexasRateV6(
    [property: JsonPropertyName("time_range")] string? TimeRange,
    [property: JsonPropertyName("base_price")] double? BasePrice, // Игнорируем
    [property: JsonPropertyName("price_5_axle")] double? Price5Axle,
    [property: JsonPropertyName("price_6_axle")] double? Price6Axle);

public record TexasRatesV4(
    [property: JsonPropertyName("2_axles_ez_tag_discount")] double? Axles2EzTagDiscount,
    [property: JsonPropertyName("2_axles_tag")] double? Axles2Tag,
    [property: JsonPropertyName("3_axles_tag")] double? Axles3Tag,
    [property: JsonPropertyName("4_axles_tag")] double? Axles4Tag,
    [property: JsonPropertyName("5_axles_tag")] double? Axles5Tag,
    [property: JsonPropertyName("6_axles_tag")] double? Axles6Tag,
    [property: JsonPropertyName("7_axles_tag")] double? Axles7Tag,
    [property: JsonPropertyName("8_axles_tag")] double? Axles8Tag);

// Модели для JSON структур
public record TexasPricesDataV1(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV1>? TollLocations);

public record TexasPricesDataV2(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV2>? TollLocations);

public record TexasPricesDataV3(
    [property: JsonPropertyName("toll_rates")] TexasTollRatesContainer? TollRates);

public record TexasPricesDataV4(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV4>? TollLocations);

public record TexasPricesDataV5(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV5>? TollLocations);

public record TexasPricesDataV6(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV6>? TollLocations);

public record TexasPricesDataV7(
    [property: JsonPropertyName("toll_locations")] List<TexasTollLocationV7>? TollLocations);

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
                case TexasJsonFormat.Format4:
                    await ProcessFormat4(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
                    break;
                case TexasJsonFormat.Format5:
                    await ProcessFormat5(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
                    break;
                case TexasJsonFormat.Format6:
                    await ProcessFormat6(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
                    break;
                case TexasJsonFormat.Format7:
                    await ProcessFormat7(jsonDoc, txBoundingBox, linkedTolls, notFoundPlazas, tollsToUpdatePrices, ct);
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
        Format2, // toll_locations с name и 5 Axle/6 Axle (TxTag, Pay by Mail)
        Format3, // toll_rates с standard_toll_rates
        Format4, // toll_locations с name и rates (2_axles_tag, 3_axles_tag, и т.д.)
        Format5, // toll_locations с location_name и rates_by_axle (массив)
        Format6, // toll_locations с location_name, schedule_type и rates (с time_range)
        Format7  // toll_locations с Toll_Gantry_Location и rates (Transponder_Rate, Pay_by_Mail_Rate)
    }

    private static TexasJsonFormat DetectFormat(JsonElement root)
    {
        // Проверяем формат 3: наличие toll_rates
        if (root.TryGetProperty("toll_rates", out _))
        {
            return TexasJsonFormat.Format3;
        }

        // Проверяем формат 1, 2 или 4: наличие toll_locations
        if (root.TryGetProperty("toll_locations", out var tollLocations) && tollLocations.ValueKind == JsonValueKind.Array)
        {
            if (tollLocations.GetArrayLength() > 0)
            {
                var firstItem = tollLocations[0];

                // Формат 7: есть Toll_Gantry_Location
                if (firstItem.TryGetProperty("Toll_Gantry_Location", out _))
                {
                    return TexasJsonFormat.Format7;
                }

                // Формат 1: есть Toll_Plaza
                if (firstItem.TryGetProperty("Toll_Plaza", out _))
                {
                    return TexasJsonFormat.Format1;
                }

                // Формат 6: есть location_name, schedule_type и rates (массив с time_range)
                if (firstItem.TryGetProperty("location_name", out _) &&
                    firstItem.TryGetProperty("schedule_type", out _) &&
                    firstItem.TryGetProperty("rates", out var ratesV6) &&
                    ratesV6.ValueKind == JsonValueKind.Array &&
                    ratesV6.GetArrayLength() > 0)
                {
                    var firstRate = ratesV6[0];
                    if (firstRate.TryGetProperty("time_range", out _) &&
                        firstRate.TryGetProperty("price_5_axle", out _))
                    {
                        return TexasJsonFormat.Format6;
                    }
                }

                // Формат 5: есть location_name и rates_by_axle
                if (firstItem.TryGetProperty("location_name", out _) && firstItem.TryGetProperty("rates_by_axle", out _))
                {
                    return TexasJsonFormat.Format5;
                }

                // Формат 4: есть name и rates (с полями типа 2_axles_tag)
                if (firstItem.TryGetProperty("name", out _) && firstItem.TryGetProperty("rates", out var rates))
                {
                    if (rates.ValueKind == JsonValueKind.Object)
                    {
                        // Проверяем наличие полей типа 2_axles_tag
                        if (rates.TryGetProperty("2_axles_tag", out _) ||
                            rates.TryGetProperty("5_axles_tag", out _) ||
                            rates.TryGetProperty("2_axles_ez_tag_discount", out _))
                        {
                            return TexasJsonFormat.Format4;
                        }
                    }
                }

                // Формат 2: есть name (без rates или с другим форматом rates)
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

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.TollPlaza, out var foundTolls) || foundTolls.Count == 0)
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

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.Name, out var foundTolls) || foundTolls.Count == 0)
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

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.TollPlaza, out var foundTolls) || foundTolls.Count == 0)
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

    private async Task ProcessFormat4(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV4>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
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

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.Name, out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.Name);
                continue;
            }

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                if (plaza.Rates == null)
                    continue;


                // Обрабатываем 5 осей
                if (plaza.Rates.Axles5Tag.HasValue && plaza.Rates.Axles5Tag.Value > 0)
                {
                    var amount = plaza.Rates.Axles5Tag.Value;
                    prices.Add(new TexasTollPriceInfo("Tag", amount, 5));

                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.EZPass,
                        AxelType: AxelType._5L,
                        Description: $"Texas {plaza.Name} - Tag (5 axles)"));
                }

                // Обрабатываем 6 осей
                if (plaza.Rates.Axles6Tag.HasValue && plaza.Rates.Axles6Tag.Value > 0)
                {
                    var amount = plaza.Rates.Axles6Tag.Value;
                    prices.Add(new TexasTollPriceInfo("Tag", amount, 6));

                    if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                    {
                        tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                    }

                    tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                        TollId: toll.Id,
                        Amount: amount,
                        PaymentType: TollPaymentType.EZPass,
                        AxelType: AxelType._6L,
                        Description: $"Texas {plaza.Name} - Tag (6 axles)"));
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

    private async Task ProcessFormat5(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV5>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.TollLocations == null || data.TollLocations.Count == 0)
        {
            return;
        }

        // Собираем все уникальные имена плаз
        var allPlazaNames = data.TollLocations
            .Where(p => !string.IsNullOrWhiteSpace(p.LocationName))
            .Select(p => p.LocationName)
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
            if (string.IsNullOrWhiteSpace(plaza.LocationName))
                continue;

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.LocationName, out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.LocationName);
                continue;
            }

            if (plaza.RatesByAxle == null || plaza.RatesByAxle.Count == 0)
                continue;

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                foreach (var rate in plaza.RatesByAxle)
                {
                    var axleCount = rate.AxleCount;

                    // Обрабатываем только 5 и 6 осей
                    if (axleCount != 5 && axleCount != 6)
                        continue;

                    // Обрабатываем Tag*
                    if (rate.Tag.HasValue && rate.Tag.Value > 0)
                    {
                        var amount = rate.Tag.Value;
                        prices.Add(new TexasTollPriceInfo("Tag", amount, axleCount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        var axelType = axleCount == 5 ? AxelType._5L : AxelType._6L;

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.EZPass,
                            AxelType: axelType,
                            Description: $"Texas {plaza.LocationName} - Tag ({axleCount} axles)"));
                    }

                    // Обрабатываем Non-Tag†
                    if (rate.NonTag.HasValue && rate.NonTag.Value > 0)
                    {
                        var amount = rate.NonTag.Value;
                        prices.Add(new TexasTollPriceInfo("Non-Tag", amount, axleCount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        var axelType = axleCount == 5 ? AxelType._5L : AxelType._6L;

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.PayOnline,
                            AxelType: axelType,
                            Description: $"Texas {plaza.LocationName} - Non-Tag ({axleCount} axles)"));
                    }
                }

                if (prices.Count > 0)
                {
                    linkedTolls.Add(new TexasLinkedTollInfo(
                        PlazaName: plaza.LocationName,
                        TollId: toll.Id,
                        TollName: toll.Name,
                        TollKey: toll.Key,
                        Prices: prices));
                }
            }
        }
    }

    private async Task ProcessFormat6(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV6>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.TollLocations == null || data.TollLocations.Count == 0)
        {
            return;
        }

        // Собираем все уникальные имена плаз
        var allPlazaNames = data.TollLocations
            .Where(p => !string.IsNullOrWhiteSpace(p.LocationName))
            .Select(p => p.LocationName)
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
            if (string.IsNullOrWhiteSpace(plaza.LocationName))
                continue;

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.LocationName, out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.LocationName);
                continue;
            }

            if (plaza.Rates == null || plaza.Rates.Count == 0)
                continue;

            // Парсим schedule_type
            var (dayOfWeekFrom, dayOfWeekTo) = ParseScheduleType(plaza.ScheduleType);

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                foreach (var rate in plaza.Rates)
                {
                    // Парсим time_range
                    var (timeFrom, timeTo) = ParseTimeRange(rate.TimeRange);

                    // Обрабатываем 5 осей
                    if (rate.Price5Axle.HasValue && rate.Price5Axle.Value > 0)
                    {
                        var amount = rate.Price5Axle.Value;
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
                            DayOfWeekFrom: dayOfWeekFrom,
                            DayOfWeekTo: dayOfWeekTo,
                            TimeFrom: timeFrom,
                            TimeTo: timeTo,
                            Description: $"Texas {plaza.LocationName} - Standard Rate (5 axles, {plaza.ScheduleType})"));
                    }

                    // Обрабатываем 6 осей
                    if (rate.Price6Axle.HasValue && rate.Price6Axle.Value > 0)
                    {
                        var amount = rate.Price6Axle.Value;
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
                            DayOfWeekFrom: dayOfWeekFrom,
                            DayOfWeekTo: dayOfWeekTo,
                            TimeFrom: timeFrom,
                            TimeTo: timeTo,
                            Description: $"Texas {plaza.LocationName} - Standard Rate (6 axles, {plaza.ScheduleType})"));
                    }
                }

                if (prices.Count > 0)
                {
                    linkedTolls.Add(new TexasLinkedTollInfo(
                        PlazaName: plaza.LocationName,
                        TollId: toll.Id,
                        TollName: toll.Name,
                        TollKey: toll.Key,
                        Prices: prices));
                }
            }
        }
    }

    private static (TollPriceDayOfWeek from, TollPriceDayOfWeek to) ParseScheduleType(string? scheduleType)
    {
        if (string.IsNullOrWhiteSpace(scheduleType))
            return (TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any);

        var scheduleLower = scheduleType.ToLower();

        if (scheduleLower.Contains("weekday"))
            return (TollPriceDayOfWeek.Monday, TollPriceDayOfWeek.Friday);

        if (scheduleLower.Contains("weekend"))
            return (TollPriceDayOfWeek.Saturday, TollPriceDayOfWeek.Sunday);

        return (TollPriceDayOfWeek.Any, TollPriceDayOfWeek.Any);
    }

    private static (TimeOnly from, TimeOnly to) ParseTimeRange(string? timeRange)
    {
        if (string.IsNullOrWhiteSpace(timeRange))
            return (default, default);

        // Формат: "12:00 AM - 04:30 AM" или "05:00 AM - 06:30 PM"
        var pattern = @"(\d{1,2}):(\d{2})\s*(AM|PM)\s*-\s*(\d{1,2}):(\d{2})\s*(AM|PM)";
        var match = Regex.Match(timeRange, pattern, RegexOptions.IgnoreCase);

        if (match.Success)
        {
            try
            {
                var fromHour = int.Parse(match.Groups[1].Value);
                var fromMinute = int.Parse(match.Groups[2].Value);
                var fromPeriod = match.Groups[3].Value.ToUpper();
                var toHour = int.Parse(match.Groups[4].Value);
                var toMinute = int.Parse(match.Groups[5].Value);
                var toPeriod = match.Groups[6].Value.ToUpper();

                // Конвертируем в 24-часовой формат
                if (fromPeriod == "PM" && fromHour != 12)
                    fromHour += 12;
                if (fromPeriod == "AM" && fromHour == 12)
                    fromHour = 0;

                if (toPeriod == "PM" && toHour != 12)
                    toHour += 12;
                if (toPeriod == "AM" && toHour == 12)
                    toHour = 0;

                var timeFrom = new TimeOnly(fromHour, fromMinute);
                var timeTo = new TimeOnly(toHour, toMinute);

                return (timeFrom, timeTo);
            }
            catch
            {
                // Если не удалось распарсить, возвращаем default
            }
        }

        return (default, default);
    }

    private async Task ProcessFormat7(
        JsonDocument jsonDoc,
        Polygon boundingBox,
        List<TexasLinkedTollInfo> linkedTolls,
        List<string> notFoundPlazas,
        Dictionary<Guid, List<TollPriceData>> tollsToUpdatePrices,
        CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<TexasPricesDataV7>(jsonDoc.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.TollLocations == null || data.TollLocations.Count == 0)
        {
            return;
        }

        // Собираем все уникальные имена плаз
        var allPlazaNames = data.TollLocations
            .Where(p => !string.IsNullOrWhiteSpace(p.TollGantryLocation))
            .Select(p => p.TollGantryLocation)
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
            if (string.IsNullOrWhiteSpace(plaza.TollGantryLocation))
                continue;

            // Ключи в словаре хранятся в оригинальном регистре
            if (!tollsByPlazaName.TryGetValue(plaza.TollGantryLocation, out var foundTolls) || foundTolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.TollGantryLocation);
                continue;
            }

            foreach (var toll in foundTolls)
            {
                var prices = new List<TexasTollPriceInfo>();

                // Обрабатываем 5 осей
                var axles5 = plaza.Rates?.Axles5;
                if (axles5 != null)
                {
                    if (axles5.TransponderRate.HasValue && axles5.TransponderRate.Value > 0)
                    {
                        var amount = axles5.TransponderRate.Value;
                        prices.Add(new TexasTollPriceInfo("Transponder", amount, 5));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.EZPass,
                            AxelType: AxelType._5L,
                            Description: $"Texas {plaza.TollGantryLocation} - Transponder (5 axles)"));
                    }

                    if (axles5.PayByMailRate.HasValue && axles5.PayByMailRate.Value > 0)
                    {
                        var amount = axles5.PayByMailRate.Value;
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
                            Description: $"Texas {plaza.TollGantryLocation} - Pay by Mail (5 axles)"));
                    }
                }

                // Обрабатываем 6 осей
                var axles6 = plaza.Rates?.Axles6AndAbove;
                if (axles6 != null)
                {
                    if (axles6.TransponderRate.HasValue && axles6.TransponderRate.Value > 0)
                    {
                        var amount = axles6.TransponderRate.Value;
                        prices.Add(new TexasTollPriceInfo("Transponder", amount, 6));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: TollPaymentType.EZPass,
                            AxelType: AxelType._6L,
                            Description: $"Texas {plaza.TollGantryLocation} - Transponder (6 axles)"));
                    }

                    if (axles6.PayByMailRate.HasValue && axles6.PayByMailRate.Value > 0)
                    {
                        var amount = axles6.PayByMailRate.Value;
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
                            Description: $"Texas {plaza.TollGantryLocation} - Pay by Mail (6 axles)"));
                    }
                }

                if (prices.Count > 0)
                {
                    linkedTolls.Add(new TexasLinkedTollInfo(
                        PlazaName: plaza.TollGantryLocation,
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

