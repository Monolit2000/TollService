using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.SC;

public record SouthCarolinaTollPlaza(
    string Name,
    SouthCarolinaRates? Rates);

public record SouthCarolinaRates(
    [property: JsonPropertyName("5_axles")] SouthCarolinaAxleRates? Axles5,
    [property: JsonPropertyName("6_axles")] SouthCarolinaAxleRates? Axles6);

public record SouthCarolinaAxleRates(
    [property: JsonPropertyName("toll_rate")] double? TollRate);

public record SouthCarolinaPricesData(
    [property: JsonPropertyName("toll_plazas")] List<SouthCarolinaTollPlaza> TollPlazas);

public record SouthCarolinaTollPriceInfo(
    string PaymentType,
    double Amount,
    int Axles);

public record SouthCarolinaLinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    List<SouthCarolinaTollPriceInfo> Prices);

public record ParseSouthCarolinaTollPricesCommand(
    string JsonPayload) : IRequest<ParseSouthCarolinaTollPricesResult>;

public record ParseSouthCarolinaTollPricesResult(
    List<SouthCarolinaLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// South Carolina bounds: (south, west, north, east) = (32.0, -83.4, 35.2, -78.5)
public class ParseSouthCarolinaTollPricesCommandHandler(
    ITollDbContext _context,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseSouthCarolinaTollPricesCommand, ParseSouthCarolinaTollPricesResult>
{
    private static readonly double ScMinLatitude = 32.0;
    private static readonly double ScMinLongitude = -83.4;
    private static readonly double ScMaxLatitude = 35.2;
    private static readonly double ScMaxLongitude = -78.5;

    public async Task<ParseSouthCarolinaTollPricesResult> Handle(ParseSouthCarolinaTollPricesCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new ParseSouthCarolinaTollPricesResult(
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            SouthCarolinaPricesData? data;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Используем JsonDocument для обработки новых полей link и payment_methods
                using (JsonDocument doc = JsonDocument.Parse(request.JsonPayload))
                {
                    // Извлекаем только поле toll_plazas для десериализации
                    if (doc.RootElement.TryGetProperty("toll_plazas", out var tollPlazasElement))
                    {
                        var tollPlazas = JsonSerializer.Deserialize<List<SouthCarolinaTollPlaza>>(tollPlazasElement.GetRawText(), options);
                        data = new SouthCarolinaPricesData(tollPlazas ?? new());
                    }
                    else
                    {
                        // Fallback: пробуем десериализовать напрямую
                        data = JsonSerializer.Deserialize<SouthCarolinaPricesData>(request.JsonPayload, options);
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                return new ParseSouthCarolinaTollPricesResult(
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            if (data?.TollPlazas == null || data.TollPlazas.Count == 0)
            {
                return new ParseSouthCarolinaTollPricesResult(
                    new(),
                    new(),
                    0,
                    "Плазы не найдены в JSON");
            }

            // Создаем bounding box для South Carolina
            var scBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                ScMinLongitude, ScMinLatitude, ScMaxLongitude, ScMaxLatitude);

            // Собираем все уникальные имена плаз для оптимизированного поиска
            var allPlazaNames = data.TollPlazas
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name)
                .Distinct()
                .ToList();

            if (allPlazaNames.Count == 0)
            {
                return new ParseSouthCarolinaTollPricesResult(
                    new(),
                    new(),
                    0,
                    "Не найдено ни одного имени плазы");
            }

            // Оптимизированный поиск tolls: один запрос к БД
            var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
                allPlazaNames,
                scBoundingBox,
                TollSearchOptions.NameOrKey,
                ct);

            var linkedTolls = new List<SouthCarolinaLinkedTollInfo>();
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
                    var prices = new List<SouthCarolinaTollPriceInfo>();

                    // Обрабатываем 5 осей
                    if (plaza.Rates?.Axles5 != null && plaza.Rates.Axles5.TollRate.HasValue && plaza.Rates.Axles5.TollRate.Value > 0)
                    {
                        var amount = plaza.Rates.Axles5.TollRate.Value;
                        var paymentType = TollPaymentType.Cash; // Используем Cash как основной тип для toll_rate
                        prices.Add(new SouthCarolinaTollPriceInfo("Toll Rate", amount, 5));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._5L,
                            Description: $"South Carolina {plaza.Name} - Toll Rate (5 axles)"));
                    }

                    // Обрабатываем 6 осей
                    if (plaza.Rates?.Axles6 != null && plaza.Rates.Axles6.TollRate.HasValue && plaza.Rates.Axles6.TollRate.Value > 0)
                    {
                        var amount = plaza.Rates.Axles6.TollRate.Value;
                        var paymentType = TollPaymentType.Cash; // Используем Cash как основной тип для toll_rate
                        prices.Add(new SouthCarolinaTollPriceInfo("Toll Rate", amount, 6));

                        if (!tollsToUpdatePrices.ContainsKey(toll.Id))
                        {
                            tollsToUpdatePrices[toll.Id] = new List<TollPriceData>();
                        }

                        tollsToUpdatePrices[toll.Id].Add(new TollPriceData(
                            TollId: toll.Id,
                            Amount: amount,
                            PaymentType: paymentType,
                            AxelType: AxelType._6L,
                            Description: $"South Carolina {plaza.Name} - Toll Rate (6 axles)"));
                    }

                    // Добавляем информацию о связанном toll с ценами
                    if (prices.Count > 0)
                    {
                        linkedTolls.Add(new SouthCarolinaLinkedTollInfo(
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

            return new ParseSouthCarolinaTollPricesResult(
                linkedTolls,
                notFoundPlazas.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new ParseSouthCarolinaTollPricesResult(
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }
}

