using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NC;

public record NorthCarolinaTollRate(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("rate_x4")] double RateX4);

public record NorthCarolinaTollPriceInfo(
    string PaymentType,
    double Amount,
    int Axles);

public record NorthCarolinaLinkedTollInfo(
    string FromPlazaName,
    Guid? FromTollId,
    string? FromTollName,
    string? FromTollKey,
    string ToPlazaName,
    Guid? ToTollId,
    string? ToTollName,
    string? ToTollKey,
    double RateX4,
    List<NorthCarolinaTollPriceInfo> Prices);

public record NorthCarolinaPaymentMethods(
    [property: JsonPropertyName("tag")] bool Tag,
    [property: JsonPropertyName("plate")] bool Plate,
    [property: JsonPropertyName("cash")] bool Cash,
    [property: JsonPropertyName("card")] bool Card,
    [property: JsonPropertyName("app")] bool App);

public record LinkNorthCarolinaTollsCommand(
    string JsonPayload) : IRequest<LinkNorthCarolinaTollsResult>;

public record LinkNorthCarolinaTollsResult(
    List<NorthCarolinaLinkedTollInfo> LinkedTolls,
    List<string> NotFoundFromPlazas,
    List<string> NotFoundToPlazas,
    int UpdatedPairsCount,
    string? Error = null);

// North Carolina bounds: (south, west, north, east) = (33.8, -84.3, 36.6, -75.4)
public class LinkNorthCarolinaTollsCommandHandler(
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkNorthCarolinaTollsCommand, LinkNorthCarolinaTollsResult>
{
    private static readonly double NcMinLatitude = 33.8;
    private static readonly double NcMinLongitude = -84.3;
    private static readonly double NcMaxLatitude = 36.6;
    private static readonly double NcMaxLongitude = -75.4;

    public async Task<LinkNorthCarolinaTollsResult> Handle(LinkNorthCarolinaTollsCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new LinkNorthCarolinaTollsResult(
                    new(),
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            List<NorthCarolinaTollRate>? tollRates = null;
            string? link = null;
            PaymentMethod? paymentMethod = null;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

            try
            {
                // Используем JsonDocument для обработки полей link и payment_methods
                using (var jsonDoc = JsonDocument.Parse(request.JsonPayload))
                {
                // Пробуем распарсить как объект с полем "routes"
                if (jsonDoc.RootElement.ValueKind == JsonValueKind.Object && jsonDoc.RootElement.TryGetProperty("routes", out var routesElement))
                {
                    tollRates = JsonSerializer.Deserialize<List<NorthCarolinaTollRate>>(routesElement.GetRawText(), options);
                }
                else
                {
                    // Пробуем распарсить как массив напрямую
                    tollRates = JsonSerializer.Deserialize<List<NorthCarolinaTollRate>>(request.JsonPayload, options);
                    }

                    // Читаем link
                    if (jsonDoc.RootElement.TryGetProperty("link", out var linkElement) && linkElement.ValueKind == JsonValueKind.String)
                    {
                        link = linkElement.GetString();
                    }

                    // Читаем payment_methods
                    if (jsonDoc.RootElement.TryGetProperty("payment_methods", out var paymentMethodsElement))
                    {
                        var paymentMethods = JsonSerializer.Deserialize<NorthCarolinaPaymentMethods>(paymentMethodsElement.GetRawText(), options);
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
                return new LinkNorthCarolinaTollsResult(
                    new(),
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            if (tollRates == null || tollRates.Count == 0)
            {
                return new LinkNorthCarolinaTollsResult(
                    new(),
                    new(),
                    new(),
                    0,
                    "Тарифы не найдены в JSON");
            }

            // Получаем или создаем StateCalculator для North Carolina
            var northCarolinaCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
                stateCode: "NC",
                calculatorName: "North Carolina Toll Facilities",
                ct);

            // Создаем bounding box для North Carolina
            var ncBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                NcMinLongitude, NcMinLatitude, NcMaxLongitude, NcMaxLatitude);

            // Собираем все уникальные имена плаз для оптимизированного поиска
            var allPlazaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rate in tollRates)
            {
                if (!string.IsNullOrWhiteSpace(rate.From))
                {
                    allPlazaNames.Add(rate.From);
                }
                if (!string.IsNullOrWhiteSpace(rate.To))
                {
                    allPlazaNames.Add(rate.To);
                }
            }

            if (allPlazaNames.Count == 0)
            {
                return new LinkNorthCarolinaTollsResult(
                    new(),
                    new(),
                    new(),
                    0,
                    "Не найдено ни одного имени плазы");
            }

            // Оптимизированный поиск tolls: один запрос к БД
            var tollsByPlazaName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
                allPlazaNames,
                ncBoundingBox,
                TollSearchOptions.NameOrKey,
                websiteUrl: link,
                paymentMethod: paymentMethod,
                ct);

            // Устанавливаем Number и StateCalculatorId для всех найденных толлов
            var allFoundTolls = new HashSet<Toll>();
            foreach (var tolls in tollsByPlazaName.Values)
            {
                foreach (var toll in tolls)
                {
                    allFoundTolls.Add(toll);
                }
            }

            // Устанавливаем Number и StateCalculatorId для каждого найденного толла
            // Используем имя плазы как Number (или извлекаем номер из скобок, если есть)
            foreach (var plazaName in allPlazaNames)
            {
                if (!tollsByPlazaName.TryGetValue(plazaName, out var tolls) || tolls.Count == 0)
                    continue;

                // Пытаемся извлечь номер из скобок "(Exit 255)" -> "255"
                var number = ExtractExitNumber(plazaName);
                if (string.IsNullOrWhiteSpace(number))
                {
                    // Если не удалось извлечь номер, используем само имя плазы
                    number = plazaName;
                }

                _tollNumberService.SetNumberAndCalculatorId(
                    tolls,
                    number,
                    northCarolinaCalculator.Id,
                    updateNumberIfDifferent: false);
            }

            var linkedTolls = new List<NorthCarolinaLinkedTollInfo>();
            var notFoundFromPlazas = new List<string>();
            var notFoundToPlazas = new List<string>();

            // Словарь для сбора пар с их TollPrice: ключ - (FromId, ToId), значение - список TollPrice
            var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

            foreach (var rate in tollRates)
            {
                if (string.IsNullOrWhiteSpace(rate.From) || string.IsNullOrWhiteSpace(rate.To))
                    continue;

                // Ищем tolls по имени плазы (ключи в словаре хранятся в оригинальном регистре)
                if (!tollsByPlazaName.TryGetValue(rate.From, out var fromTolls) || fromTolls.Count == 0)
                {
                    notFoundFromPlazas.Add(rate.From);
                    continue;
                }

                if (!tollsByPlazaName.TryGetValue(rate.To, out var toTolls) || toTolls.Count == 0)
                {
                    notFoundToPlazas.Add(rate.To);
                    continue;
                }

                // Обрабатываем все комбинации from -> to толлов
                foreach (var fromToll in fromTolls)
                {
                    foreach (var toToll in toTolls)
                    {
                        if (fromToll.Id == toToll.Id)
                        {
                            continue;
                        }

                        var pairKey = (fromToll.Id, toToll.Id);
                        if (!tollPairsWithPrices.ContainsKey(pairKey))
                        {
                            tollPairsWithPrices[pairKey] = new List<TollPrice>();
                        }

                        // Создаем цены для 5 и 6 осей
                        // rate_x4 - это цена для 4 осей, используем её для 5 и 6 осей
                        var prices = new List<NorthCarolinaTollPriceInfo>();
                        var description = $"North Carolina - {rate.From} -> {rate.To}";

                        // Создаем TollPrice для 5 осей (Cash)
                        var tollPrice5 = new TollPrice
                        {
                            Id = Guid.NewGuid(),
                            TollId = fromToll.Id,
                            PaymentType = TollPaymentType.Cash,
                            AxelType = AxelType._5L,
                            Amount = rate.RateX4,
                            TimeOfDay = TollPriceTimeOfDay.Any,
                            Description = $"{description} - Cash (5 axles)"
                        };
                        tollPairsWithPrices[pairKey].Add(tollPrice5);
                        prices.Add(new NorthCarolinaTollPriceInfo("Cash", rate.RateX4, 5));

                        // Создаем TollPrice для 6 осей (Cash)
                        var tollPrice6 = new TollPrice
                        {
                            Id = Guid.NewGuid(),
                            TollId = fromToll.Id,
                            PaymentType = TollPaymentType.Cash,
                            AxelType = AxelType._6L,
                            Amount = rate.RateX4,
                            TimeOfDay = TollPriceTimeOfDay.Any,
                            Description = $"{description} - Cash (6 axles)"
                        };
                        tollPairsWithPrices[pairKey].Add(tollPrice6);
                        prices.Add(new NorthCarolinaTollPriceInfo("Cash", rate.RateX4, 6));

                        // Добавляем информацию о связанном toll с ценами
                        linkedTolls.Add(new NorthCarolinaLinkedTollInfo(
                            FromPlazaName: rate.From,
                            FromTollId: fromToll.Id,
                            FromTollName: fromToll.Name,
                            FromTollKey: fromToll.Key,
                            ToPlazaName: rate.To,
                            ToTollId: toToll.Id,
                            ToTollName: toToll.Name,
                            ToTollKey: toToll.Key,
                            RateX4: rate.RateX4,
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
                    northCarolinaCalculator.Id,
                    includeTollPrices: true,
                    ct);

                updatedPairsCount = result.Count;
            }

            // Сохраняем все изменения
            await _context.SaveChangesAsync(ct);

            return new LinkNorthCarolinaTollsResult(
                linkedTolls,
                notFoundFromPlazas.Distinct().ToList(),
                notFoundToPlazas.Distinct().ToList(),
                updatedPairsCount);
        }
        catch (Exception ex)
        {
            return new LinkNorthCarolinaTollsResult(
                new(),
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }

    /// <summary>
    /// Извлекает номер выхода из имени плазы, например "U.S. 74 (West) (Exit 255)" -> "255"
    /// </summary>
    private static string? ExtractExitNumber(string plazaName)
    {
        if (string.IsNullOrWhiteSpace(plazaName))
            return null;

        // Ищем паттерн "(Exit 255)" или "(Exit 255)" и извлекаем число
        var match = System.Text.RegularExpressions.Regex.Match(plazaName, @"\(Exit\s+(\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
}

