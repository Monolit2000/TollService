using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.VA;

public record VirginiaTollPlaza(
    string Name,
    VirginiaRates? Rates);

public record VirginiaRates(
    [property: JsonPropertyName("5_axles")] VirginiaAxleRates? Axles5,
    [property: JsonPropertyName("6_axles")] VirginiaAxleRates? Axles6);

public record VirginiaAxleRates(
    [property: JsonPropertyName("cash_out_of_state_ezpass")] double? CashOutOfStateEzpass,
    [property: JsonPropertyName("va_ezpass")] double? VaEzpass,
    [property: JsonPropertyName("ezpass_rate")] double? EzpassRate,
    [property: JsonPropertyName("pay_by_plate_rate")] double? PayByPlateRate);

public record VirginiaPricesData(
    [property: JsonPropertyName("toll_plazas")] List<VirginiaTollPlaza> TollPlazas);

public record VirginiaPaymentMethods(
    [property: JsonPropertyName("tag")] bool Tag,
    [property: JsonPropertyName("plate")] bool Plate,
    [property: JsonPropertyName("cash")] bool Cash,
    [property: JsonPropertyName("card")] bool Card,
    [property: JsonPropertyName("app")] bool App);

public record VirginiaTollPriceInfo(
    string PaymentType,
    double Amount,
    int Axles);

public record VirginiaLinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    List<VirginiaTollPriceInfo> Prices);

public record ParseVirginiaTollPricesCommand(
    string JsonPayload) : IRequest<ParseVirginiaTollPricesResult>;

public record ParseVirginiaTollPricesResult(
    List<VirginiaLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// Virginia bounds: (south, west, north, east) = (36.5, -83.7, 39.5, -75.2)
public class ParseVirginiaTollPricesCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseVirginiaTollPricesCommand, ParseVirginiaTollPricesResult>
{
    private static readonly double VaMinLatitude = 36.5;
    private static readonly double VaMinLongitude = -83.7;
    private static readonly double VaMaxLatitude = 39.5;
    private static readonly double VaMaxLongitude = -75.2;

    public async Task<ParseVirginiaTollPricesResult> Handle(ParseVirginiaTollPricesCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new ParseVirginiaTollPricesResult(
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            VirginiaPricesData? data = null;
            string? link = null;
            PaymentMethod? paymentMethod = null;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

            try
            {
                // Используем JsonDocument для обработки новых полей link и payment_methods
                using (JsonDocument doc = JsonDocument.Parse(request.JsonPayload))
                {
                    // Проверяем формат Dulles Greenway (с полем "tolls")
                    if (doc.RootElement.TryGetProperty("tolls", out var tollsElement))
                    {
                        // Конвертируем формат Dulles Greenway в стандартный формат
                        data = ConvertDullesGreenwayFormat(tollsElement, options);
                    }
                    // Стандартный формат с toll_plazas
                    else if (doc.RootElement.TryGetProperty("toll_plazas", out var tollPlazasElement))
                    {
                        var tollPlazas = JsonSerializer.Deserialize<List<VirginiaTollPlaza>>(tollPlazasElement.GetRawText(), options);
                        data = new VirginiaPricesData(tollPlazas ?? new());
                    }
                    else
                    {
                        // Fallback: пробуем десериализовать напрямую
                        data = JsonSerializer.Deserialize<VirginiaPricesData>(request.JsonPayload, options);
                    }

                    // Читаем link
                    if (doc.RootElement.TryGetProperty("link", out var linkElement) && linkElement.ValueKind == JsonValueKind.String)
                    {
                        link = linkElement.GetString();
                    }

                    // Читаем payment_methods
                    if (doc.RootElement.TryGetProperty("payment_methods", out var paymentMethodsElement))
                    {
                        var paymentMethods = JsonSerializer.Deserialize<VirginiaPaymentMethods>(paymentMethodsElement.GetRawText(), options);
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
                return new ParseVirginiaTollPricesResult(
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            if (data?.TollPlazas == null || data.TollPlazas.Count == 0)
            {
                return new ParseVirginiaTollPricesResult(
                    new(),
                    new(),
                    0,
                    "Плазы не найдены в JSON");
            }

            // Создаем bounding box для Virginia
            var vaBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                VaMinLongitude, VaMinLatitude, VaMaxLongitude, VaMaxLatitude);

            // Собираем все уникальные имена плаз для оптимизированного поиска
            var allPlazaNames = data.TollPlazas
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name)
                .Distinct()
                .ToList();

            if (allPlazaNames.Count == 0)
            {
                return new ParseVirginiaTollPricesResult(
                    new(),
                    new(),
                    0,
                    "Не найдено ни одного имени плазы");
            }

            // Оптимизированный поиск tolls: один запрос к БД
            var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
                allPlazaNames,
                vaBoundingBox,
                TollSearchOptions.NameOrKey,
                websiteUrl: link,
                paymentMethod: paymentMethod,
                ct);

            var linkedTolls = new List<VirginiaLinkedTollInfo>();
            var notFoundPlazas = new List<string>();
            var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

            foreach (var plaza in data.TollPlazas)
            {
                if (string.IsNullOrWhiteSpace(plaza.Name))
                    continue;

                // Ищем tolls по имени плазы (ключи в словаре хранятся в оригинальном регистре)
                if (!tollsByPlazaName.TryGetValue(plaza.Name, out var foundTolls) || foundTolls.Count == 0)
                {
                    notFoundPlazas.Add(plaza.Name);
                    continue;
                }

                // Обрабатываем цены для каждой найденной плазы
                foreach (var toll in foundTolls)
                {
                    var prices = new List<VirginiaTollPriceInfo>();

                    // Обрабатываем 5 осей
                    if (plaza.Rates?.Axles5 != null)
                    {
                        var rates5 = plaza.Rates.Axles5;

                        // Cash / Out of State E-ZPass
                        if (rates5.CashOutOfStateEzpass.HasValue && rates5.CashOutOfStateEzpass.Value > 0)
                        {
                            var paymentType = TollPaymentType.OutOfStateEZPass;
                            var amount = rates5.CashOutOfStateEzpass.Value;
                            prices.Add(new VirginiaTollPriceInfo("Out of State E-ZPass", amount, 5));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._5L,
                                Description: $"Virginia {plaza.Name} - Out of State E-ZPass (5 axles)"));
                        }

                        // VA E-ZPass
                        if (rates5.VaEzpass.HasValue && rates5.VaEzpass.Value > 0)
                        {
                            var paymentType = TollPaymentType.EZPass;
                            var amount = rates5.VaEzpass.Value;
                            prices.Add(new VirginiaTollPriceInfo("VA E-ZPass", amount, 5));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._5L,
                                Description: $"Virginia {plaza.Name} - VA E-ZPass (5 axles)"));
                        }

                        // E-ZPass Rate (для Dulles Toll Road)
                        if (rates5.EzpassRate.HasValue && rates5.EzpassRate.Value > 0)
                        {
                            var paymentType = TollPaymentType.EZPass;
                            var amount = rates5.EzpassRate.Value;
                            prices.Add(new VirginiaTollPriceInfo("E-ZPass", amount, 5));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._5L,
                                Description: $"Virginia {plaza.Name} - E-ZPass (5 axles)"));
                        }

                        // Pay-by-Plate Rate (для Dulles Toll Road)
                        if (rates5.PayByPlateRate.HasValue && rates5.PayByPlateRate.Value > 0)
                        {
                            var paymentType = TollPaymentType.PayOnline;
                            var amount = rates5.PayByPlateRate.Value;
                            prices.Add(new VirginiaTollPriceInfo("Pay-by-Plate", amount, 5));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._5L,
                                Description: $"Virginia {plaza.Name} - Pay-by-Plate (5 axles)"));
                        }
                    }

                    // Обрабатываем 6 осей
                    if (plaza.Rates?.Axles6 != null)
                    {
                        var rates6 = plaza.Rates.Axles6;

                        // Cash / Out of State E-ZPass
                        if (rates6.CashOutOfStateEzpass.HasValue && rates6.CashOutOfStateEzpass.Value > 0)
                        {
                            var paymentType = TollPaymentType.OutOfStateEZPass;
                            var amount = rates6.CashOutOfStateEzpass.Value;
                            prices.Add(new VirginiaTollPriceInfo("Out of State E-ZPass", amount, 6));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._6L,
                                Description: $"Virginia {plaza.Name} - Out of State E-ZPass (6 axles)"));
                        }

                        // VA E-ZPass
                        if (rates6.VaEzpass.HasValue && rates6.VaEzpass.Value > 0)
                        {
                            var paymentType = TollPaymentType.EZPass;
                            var amount = rates6.VaEzpass.Value;
                            prices.Add(new VirginiaTollPriceInfo("VA E-ZPass", amount, 6));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._6L,
                                Description: $"Virginia {plaza.Name} - VA E-ZPass (6 axles)"));
                        }

                        // E-ZPass Rate (для Dulles Toll Road)
                        if (rates6.EzpassRate.HasValue && rates6.EzpassRate.Value > 0)
                        {
                            var paymentType = TollPaymentType.EZPass;
                            var amount = rates6.EzpassRate.Value;
                            prices.Add(new VirginiaTollPriceInfo("E-ZPass", amount, 6));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._6L,
                                Description: $"Virginia {plaza.Name} - E-ZPass (6 axles)"));
                        }

                        // Pay-by-Plate Rate (для Dulles Toll Road)
                        if (rates6.PayByPlateRate.HasValue && rates6.PayByPlateRate.Value > 0)
                        {
                            var paymentType = TollPaymentType.PayOnline;
                            var amount = rates6.PayByPlateRate.Value;
                            prices.Add(new VirginiaTollPriceInfo("Pay-by-Plate", amount, 6));

                            if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                            {
                                tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                            }

                            tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                                TollId: toll.Id,
                                Amount: amount,
                                PaymentType: paymentType,
                                AxelType: AxelType._6L,
                                Description: $"Virginia {plaza.Name} - Pay-by-Plate (6 axles)"));
                        }
                    }

                    // Добавляем информацию о связанном toll с ценами
                    if (prices.Count > 0)
                    {
                        linkedTolls.Add(new VirginiaLinkedTollInfo(
                            PlazaName: plaza.Name,
                            TollId: toll.Id,
                            TollName: toll.Name,
                            TollKey: toll.Key,
                            Prices: prices));
                    }
                }
            }

            // Батч-установка цен
            int updatedTollsCount = 0;
            if (tollsToUpdatePrices.Count > 0)
            {
                // Конвертируем List в IEnumerable для метода
                var tollsToUpdatePricesEnumerable = tollsToUpdatePrices.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IEnumerable<TollPriceData>)kvp.Value);

                var updatedPricesResult = await _calculatePriceService.SetTollPricesDirectlyBatchAsync(
                    tollsToUpdatePricesEnumerable,
                    null,
                    ct);
                updatedTollsCount = updatedPricesResult.Count;
            }

            // Сохраняем все изменения
            await _context.SaveChangesAsync(ct);

            return new ParseVirginiaTollPricesResult(
                linkedTolls,
                notFoundPlazas.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new ParseVirginiaTollPricesResult(
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }

    /// <summary>
    /// Конвертирует формат Dulles Greenway (tolls с exit-1, exit-2 и т.д.) в стандартный формат VirginiaPricesData
    /// </summary>
    private static VirginiaPricesData ConvertDullesGreenwayFormat(JsonElement tollsElement, JsonSerializerOptions options)
    {
        var tollPlazas = new List<VirginiaTollPlaza>();

        foreach (var exitProperty in tollsElement.EnumerateObject())
        {
            var exitData = exitProperty.Value;
            if (!exitData.TryGetProperty("name", out var nameElement))
                continue;

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Извлекаем цены для 5 и 6 осей
            // Используем notcongested-ez-5axle и notcongested-ez-6axle как основные цены E-ZPass
            // Используем notcongested-cc-5axle и notcongested-cc-6axle как цены для кредитных карт
            var rates5 = new VirginiaAxleRates(
                CashOutOfStateEzpass: null,
                VaEzpass: ParsePriceValue(exitData, "notcongested-ez-5axle"),
                EzpassRate: ParsePriceValue(exitData, "notcongested-ez-5axle"),
                PayByPlateRate: ParsePriceValue(exitData, "notcongested-cc-5axle"));

            var rates6 = new VirginiaAxleRates(
                CashOutOfStateEzpass: null,
                VaEzpass: ParsePriceValue(exitData, "notcongested-ez-6axle"),
                EzpassRate: ParsePriceValue(exitData, "notcongested-ez-6axle"),
                PayByPlateRate: ParsePriceValue(exitData, "notcongested-cc-6axle"));

            var rates = new VirginiaRates(rates5, rates6);
            tollPlazas.Add(new VirginiaTollPlaza(name, rates));
        }

        return new VirginiaPricesData(tollPlazas);
    }

    /// <summary>
    /// Парсит значение цены из JSON элемента, обрабатывая "NO TOLL" как null
    /// </summary>
    private static double? ParsePriceValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var priceElement))
            return null;

        if (priceElement.ValueKind == JsonValueKind.String)
        {
            var stringValue = priceElement.GetString();
            if (string.IsNullOrWhiteSpace(stringValue) || stringValue.Equals("NO TOLL", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        if (priceElement.ValueKind == JsonValueKind.Number)
        {
            return priceElement.GetDouble();
        }

        if (priceElement.ValueKind == JsonValueKind.String)
        {
            var stringValue = priceElement.GetString();
            if (double.TryParse(stringValue, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))
            {
                return parsedValue;
            }
        }

        return null;
    }
}

