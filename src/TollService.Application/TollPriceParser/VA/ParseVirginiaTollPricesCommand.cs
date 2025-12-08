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
    [property: JsonPropertyName("va_ezpass")] double? VaEzpass);

public record VirginiaPricesData(
    [property: JsonPropertyName("toll_plazas")] List<VirginiaTollPlaza> TollPlazas);

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

            VirginiaPricesData? data;
            try
            {
                data = JsonSerializer.Deserialize<VirginiaPricesData>(request.JsonPayload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
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
                ct);

            var linkedTolls = new List<VirginiaLinkedTollInfo>();
            var notFoundPlazas = new List<string>();
            var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

            foreach (var plaza in data.TollPlazas)
            {
                if (string.IsNullOrWhiteSpace(plaza.Name))
                    continue;

                // Ищем tolls по имени плазы
                if (!tollsByPlazaName.TryGetValue(plaza.Name.ToLower(), out var foundTolls) || foundTolls.Count == 0)
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
}

