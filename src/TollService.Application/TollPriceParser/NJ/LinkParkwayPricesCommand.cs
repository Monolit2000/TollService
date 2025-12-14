using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record ParkwayTollPlaza(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("milepost")] double? Milepost,
    [property: JsonPropertyName("directions")] string? Directions,
    [property: JsonPropertyName("rates")] ParkwayRates? Rates);

public record ParkwayRates(
    [property: JsonPropertyName("cash")] double? Cash,
    [property: JsonPropertyName("ez_pass_peak")] double? EzPassPeak,
    [property: JsonPropertyName("ez_pass_off_peak_truck")] double? EzPassOffPeakTruck);

public record ParkwayPricesData(
    [property: JsonPropertyName("road_name")] string? RoadName,
    [property: JsonPropertyName("year")] string? Year,
    [property: JsonPropertyName("vehicle_class")] string? VehicleClass,
    [property: JsonPropertyName("axles")] int? Axles,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("toll_plazas")] List<ParkwayTollPlaza>? TollPlazas);

public record ParkwayTollPriceInfo(
    string PaymentType,
    double Amount);

public record ParkwayLinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    List<ParkwayTollPriceInfo> Prices);

public record LinkParkwayPricesCommand(string JsonPayload)
    : IRequest<LinkParkwayPricesResult>;

public record LinkParkwayPricesResult(
    List<ParkwayLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
public class LinkParkwayPricesCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkParkwayPricesCommand, LinkParkwayPricesResult>
{
    private static readonly double NjMinLatitude = 38.9;
    private static readonly double NjMinLongitude = -75.6;
    private static readonly double NjMaxLatitude = 41.4;
    private static readonly double NjMaxLongitude = -73.9;

    public async Task<LinkParkwayPricesResult> Handle(LinkParkwayPricesCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new LinkParkwayPricesResult(
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            ParkwayPricesData? data;
            try
            {
                data = JsonSerializer.Deserialize<ParkwayPricesData>(request.JsonPayload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jsonEx)
            {
                return new LinkParkwayPricesResult(
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            if (data?.TollPlazas == null || data.TollPlazas.Count == 0)
            {
                return new LinkParkwayPricesResult(
                    new(),
                    new(),
                    0,
                    "Плазы не найдены в JSON");
            }

            // Создаем bounding box для New Jersey
            var njBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                NjMinLongitude, NjMinLatitude, NjMaxLongitude, NjMaxLatitude);

            // Собираем все уникальные имена плаз для оптимизированного поиска
            var allPlazaNames = data.TollPlazas
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name)
                .Distinct()
                .ToList();

            if (allPlazaNames.Count == 0)
            {
                return new LinkParkwayPricesResult(
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
                websiteUrl: null,
                paymentMethod: null,
                ct);

            var linkedTolls = new List<ParkwayLinkedTollInfo>();
            var notFoundPlazas = new List<string>();
            var tollsToUpdatePrices = new Dictionary<Guid, List<TollPriceData>>();

            foreach (var plaza in data.TollPlazas)
            {
                if (string.IsNullOrWhiteSpace(plaza.Name))
                {
                    notFoundPlazas.Add("Plaza with empty name");
                    continue;
                }

                // Ищем tolls по имени плазы (ключи в словаре хранятся в оригинальном регистре)
                if (!tollsByPlazaName.TryGetValue(plaza.Name, out var foundTolls) || foundTolls.Count == 0)
                {
                    notFoundPlazas.Add(plaza.Name);
                    continue;
                }

                // Обрабатываем цены для каждой найденной плазы
                foreach (var toll in foundTolls)
                {
                    var prices = new List<ParkwayTollPriceInfo>();

                    // Обрабатываем Cash
                    if (plaza.Rates?.Cash.HasValue == true && plaza.Rates.Cash.Value > 0)
                    {
                        var paymentType = TollPaymentType.Cash;
                        var amount = plaza.Rates.Cash.Value;
                        prices.Add(new ParkwayTollPriceInfo("Cash", amount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._5L,
                            Description: $"New Jersey Parkway {plaza.Name} - Cash"));
                    }

                    // Обрабатываем EZPass Peak
                    if (plaza.Rates?.EzPassPeak.HasValue == true && plaza.Rates.EzPassPeak.Value > 0)
                    {
                        var paymentType = TollPaymentType.EZPass;
                        var amount = plaza.Rates.EzPassPeak.Value;
                        prices.Add(new ParkwayTollPriceInfo("EZPass Peak", amount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._5L,
                            Description: $"New Jersey Parkway {plaza.Name} - EZPass Peak"));
                    }

                    // Обрабатываем EZPass Off-Peak Truck
                    if (plaza.Rates?.EzPassOffPeakTruck.HasValue == true && plaza.Rates.EzPassOffPeakTruck.Value > 0)
                    {
                        var paymentType = TollPaymentType.EZPass;
                        var amount = plaza.Rates.EzPassOffPeakTruck.Value;
                        prices.Add(new ParkwayTollPriceInfo("EZPass Off-Peak Truck", amount));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._5L,
                            Description: $"New Jersey Parkway {plaza.Name} - EZPass Off-Peak Truck"));
                    }

                    // Добавляем информацию о связанном toll с ценами
                    if (prices.Count > 0)
                    {
                        linkedTolls.Add(new ParkwayLinkedTollInfo(
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

            return new LinkParkwayPricesResult(
                linkedTolls,
                notFoundPlazas.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new LinkParkwayPricesResult(
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }
}

