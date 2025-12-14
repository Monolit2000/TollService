using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record AtlanticExpresswayTollRate(
    [property: JsonPropertyName("plaza_name")] string PlazaName,
    [property: JsonPropertyName("entry_name")] string? EntryName,
    [property: JsonPropertyName("classification")] string? Classification,
    [property: JsonPropertyName("cash")] double? Cash,
    [property: JsonPropertyName("ez_pass_frequent_user")] double? EzPassFrequentUser);

public record AtlanticExpresswayPricesData(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("road")] string? Road,
    [property: JsonPropertyName("vehicle_class_id")] int? VehicleClassId,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("total_checked")] int? TotalChecked,
    [property: JsonPropertyName("toll_rates")] List<AtlanticExpresswayTollRate>? TollRates);

public record AtlanticExpresswayPaymentMethods(
    [property: JsonPropertyName("tag")] bool Tag,
    [property: JsonPropertyName("plate")] bool Plate,
    [property: JsonPropertyName("cash")] bool Cash,
    [property: JsonPropertyName("card")] bool Card,
    [property: JsonPropertyName("app")] bool App);

public record AtlanticExpresswayTollPriceInfo(
    string PaymentType,
    double Amount);

public record AtlanticExpresswayLinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    List<AtlanticExpresswayTollPriceInfo> Prices);

public record LinkAtlanticExpresswayPricesCommand(string JsonPayload)
    : IRequest<LinkAtlanticExpresswayPricesResult>;

public record LinkAtlanticExpresswayPricesResult(
    List<AtlanticExpresswayLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
public class LinkAtlanticExpresswayPricesCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkAtlanticExpresswayPricesCommand, LinkAtlanticExpresswayPricesResult>
{
    private static readonly double NjMinLatitude = 38.9;
    private static readonly double NjMinLongitude = -75.6;
    private static readonly double NjMaxLatitude = 41.4;
    private static readonly double NjMaxLongitude = -73.9;

    public async Task<LinkAtlanticExpresswayPricesResult> Handle(LinkAtlanticExpresswayPricesCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new LinkAtlanticExpresswayPricesResult(
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            AtlanticExpresswayPricesData? data = null;
            string? link = null;
            PaymentMethod? paymentMethod = null;
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            try
            {
                // Используем JsonDocument для обработки полей link и payment_methods
                using (JsonDocument doc = JsonDocument.Parse(request.JsonPayload))
                {
                    // Десериализуем основные данные
                    data = JsonSerializer.Deserialize<AtlanticExpresswayPricesData>(request.JsonPayload, options);

                    // Читаем link
                    if (doc.RootElement.TryGetProperty("link", out var linkElement) && linkElement.ValueKind == JsonValueKind.String)
                    {
                        link = linkElement.GetString();
                    }

                    // Читаем payment_methods
                    if (doc.RootElement.TryGetProperty("payment_methods", out var paymentMethodsElement))
                    {
                        var paymentMethods = JsonSerializer.Deserialize<AtlanticExpresswayPaymentMethods>(paymentMethodsElement.GetRawText(), options);
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
                return new LinkAtlanticExpresswayPricesResult(
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            if (data?.TollRates == null || data.TollRates.Count == 0)
            {
                return new LinkAtlanticExpresswayPricesResult(
                    new(),
                    new(),
                    0,
                    "Плазы не найдены в JSON");
            }

            // Создаем bounding box для New Jersey
            var njBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                NjMinLongitude, NjMinLatitude, NjMaxLongitude, NjMaxLatitude);

            // Собираем все уникальные имена плаз для оптимизированного поиска
            var allPlazaNames = data.TollRates
                .Where(p => !string.IsNullOrWhiteSpace(p.PlazaName))
                .Select(p => p.PlazaName)
                .Distinct()
                .ToList();

            if (allPlazaNames.Count == 0)
            {
                return new LinkAtlanticExpresswayPricesResult(
                    new(),
                    new(),
                    0,
                    "Не найдено ни одного имени плазы");
            }

            // Оптимизированный поиск tolls: один запрос к БД
            var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
                allPlazaNames,
                njBoundingBox,
                TollSearchOptions.NameOrKey,
                websiteUrl: link,
                paymentMethod: paymentMethod,
                ct);

            var linkedTolls = new List<AtlanticExpresswayLinkedTollInfo>();
            var notFoundPlazas = new List<string>();
            var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

            foreach (var rate in data.TollRates)
            {
                if (string.IsNullOrWhiteSpace(rate.PlazaName))
                {
                    notFoundPlazas.Add("Plaza with empty name");
                    continue;
                }

                // Ищем tolls по имени плазы (ключи в словаре хранятся в оригинальном регистре)
                if (!tollsByPlazaName.TryGetValue(rate.PlazaName, out var foundTolls) || foundTolls.Count == 0)
                {
                    notFoundPlazas.Add(rate.PlazaName);
                    continue;
                }

                // Обрабатываем цены для каждой найденной плазы
                foreach (var toll in foundTolls)
                {
                    var prices = new List<AtlanticExpresswayTollPriceInfo>();

                    // Обрабатываем Cash
                    if (rate.Cash.HasValue && rate.Cash.Value > 0)
                    {
                        var paymentType = TollPaymentType.Cash;
                        var amount = rate.Cash.Value;
                        prices.Add(new AtlanticExpresswayTollPriceInfo("Cash", amount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._5L,
                            Description: $"New Jersey Atlantic Expressway {rate.PlazaName} - Cash"));
                    }

                    // Обрабатываем EZPass Frequent User
                    if (rate.EzPassFrequentUser.HasValue && rate.EzPassFrequentUser.Value > 0)
                    {
                        var paymentType = TollPaymentType.EZPass;
                        var amount = rate.EzPassFrequentUser.Value;
                        prices.Add(new AtlanticExpresswayTollPriceInfo("EZPass Frequent User", amount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._5L,
                            Description: $"New Jersey Atlantic Expressway {rate.PlazaName} - EZPass Frequent User"));
                    }

                    // Добавляем информацию о связанном toll с ценами
                    if (prices.Count > 0)
                    {
                        linkedTolls.Add(new AtlanticExpresswayLinkedTollInfo(
                            PlazaName: rate.PlazaName,
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

            return new LinkAtlanticExpresswayPricesResult(
                linkedTolls,
                notFoundPlazas.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new LinkAtlanticExpresswayPricesResult(
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }
}

