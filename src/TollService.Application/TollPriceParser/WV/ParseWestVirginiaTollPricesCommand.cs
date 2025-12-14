using MediatR;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.WV;

public record ParseWestVirginiaTollPricesCommand(
    string Url = "https://transportation.wv.gov/Turnpike/travel_resources/Pages/Toll-Rates-2025.aspx")
    : IRequest<ParseWestVirginiaTollPricesResult>;

public record WestVirginiaTollPriceInfo(
    int TollClass,
    int Axles,
    string VehicleType,
    string PlazaType,
    double CashRate,
    double WVEZPassCommercialRate,
    double NonWVEZPassRate,
    double? PayByPlateRate);

public record WestVirginiaLinkedTollInfo(
    string PlazaType,
    Guid TollId,
    string? TollName,
    string? TollKey,
    int TollClass,
    int Axles);

public record ParseWestVirginiaTollPricesResult(
    List<WestVirginiaTollPriceInfo> Prices,
    List<WestVirginiaLinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedTollsCount,
    string? Error = null);

// West Virginia bounds: (south, west, north, east) = (37.2, -82.7, 40.6, -77.7)
public class ParseWestVirginiaTollPricesCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory,
    TollSearchService _tollSearchService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseWestVirginiaTollPricesCommand, ParseWestVirginiaTollPricesResult>
{
    private static readonly double WvMinLatitude = 37.2;
    private static readonly double WvMinLongitude = -82.7;
    private static readonly double WvMaxLatitude = 40.6;
    private static readonly double WvMaxLongitude = -77.7;

    public async Task<ParseWestVirginiaTollPricesResult> Handle(ParseWestVirginiaTollPricesCommand request, CancellationToken ct)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            var html = await httpClient.GetStringAsync(request.Url, ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var prices = new List<WestVirginiaTollPriceInfo>();

            // Находим таблицу с ценами
            var table = doc.DocumentNode.SelectSingleNode("//table[@class='ms-rteTable-default']");
            if (table == null)
            {
                return new ParseWestVirginiaTollPricesResult(
                    prices,
                    new List<WestVirginiaLinkedTollInfo>(),
                    new List<string>(),
                    0,
                    "Таблица с ценами не найдена на странице");
            }

            var rows = table.SelectNodes(".//tr");
            if (rows == null)
            {
                return new ParseWestVirginiaTollPricesResult(
                    prices,
                    new List<WestVirginiaLinkedTollInfo>(),
                    new List<string>(),
                    0,
                    "Строки таблицы не найдены");
            }

            // Ищем строки с классами 8 и 9
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 9)
                    continue;

                // Извлекаем Toll Class из первого столбца
                var tollClassText = ExtractText(cells[0]);
                if (!int.TryParse(tollClassText, out var tollClass) || (tollClass != 8 && tollClass != 9))
                    continue;

                // Извлекаем количество осей
                var axlesText = ExtractText(cells[1]);
                var axles = ParseAxles(axlesText);

                // Извлекаем тип транспортного средства
                var vehicleType = ExtractText(cells[2]);

                // Парсим цены для Mainline (столбцы 3, 4, 5)
                var mainlineCash = ParsePrice(cells[3]);
                var mainlineWVEZPass = ParsePrice(cells[4]);
                var mainlineNonWVEZPass = ParsePrice(cells[5]);

                if (mainlineCash > 0 || mainlineWVEZPass > 0 || mainlineNonWVEZPass > 0)
                {
                    prices.Add(new WestVirginiaTollPriceInfo(
                        TollClass: tollClass,
                        Axles: axles,
                        VehicleType: vehicleType,
                        PlazaType: "Mainline",
                        CashRate: mainlineCash,
                        WVEZPassCommercialRate: mainlineWVEZPass,
                        NonWVEZPassRate: mainlineNonWVEZPass,
                        PayByPlateRate: null));
                }

                // Парсим цены для North Beckley (столбцы 6, 7, 8)
                var northBeckleyPayByPlate = ParsePrice(cells[6]);
                var northBeckleyWVEZPass = ParsePrice(cells[7]);
                var northBeckleyNonWVEZPass = ParsePrice(cells[8]);

                if (northBeckleyPayByPlate > 0 || northBeckleyWVEZPass > 0 || northBeckleyNonWVEZPass > 0)
                {
                    prices.Add(new WestVirginiaTollPriceInfo(
                        TollClass: tollClass,
                        Axles: axles,
                        VehicleType: vehicleType,
                        PlazaType: "North Beckley",
                        CashRate: 0, // Pay-by-Plate используется вместо Cash
                        WVEZPassCommercialRate: northBeckleyWVEZPass,
                        NonWVEZPassRate: northBeckleyNonWVEZPass,
                        PayByPlateRate: northBeckleyPayByPlate));
                }
            }

            // Если цены найдены, ищем tolls и устанавливаем цены
            var linkedTolls = new List<WestVirginiaLinkedTollInfo>();
            var notFoundPlazas = new List<string>();
            int updatedTollsCount = 0;

            if (prices.Count > 0)
            {
                // Создаем bounding box для West Virginia
                var wvBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                    WvMinLongitude, WvMinLatitude, WvMaxLongitude, WvMaxLatitude);

                // Группируем цены по PlazaType
                var pricesByPlaza = prices.GroupBy(p => p.PlazaType);

                var tollPricesByTollId = new Dictionary<Guid, List<TollPriceData>>();

                foreach (var plazaGroup in pricesByPlaza)
                {
                    var plazaType = plazaGroup.Key;
                    var plazaPrices = plazaGroup.ToList();

                    // Ищем tolls по названию плазы
                    var foundTolls = await _tollSearchService.FindTollsInBoundingBoxAsync(
                        plazaType,
                        wvBoundingBox,
                        TollSearchOptions.NameOrKey,
                        ct);

                    if (foundTolls.Count == 0)
                    {
                        notFoundPlazas.Add(plazaType);
                        continue;
                    }

                    // Для каждого найденного toll создаем TollPriceData
                    foreach (var toll in foundTolls)
                    {
                        if (!tollPricesByTollId.ContainsKey(toll.Id))
                        {
                            tollPricesByTollId[toll.Id] = new List<TollPriceData>();
                        }

                        foreach (var priceInfo in plazaPrices)
                        {
                            var axelType = priceInfo.Axles switch
                            {
                                5 => AxelType._5L,
                                6 => AxelType._6L,
                                _ => AxelType._5L
                            };

                            // Добавляем Cash Rate
                            if (priceInfo.CashRate > 0)
                            {
                                tollPricesByTollId[toll.Id].Add(new TollPriceData(
                                    TollId: toll.Id,
                                    Amount: priceInfo.CashRate,
                                    PaymentType: TollPaymentType.Cash,
                                    AxelType: axelType,
                                    Description: $"West Virginia Turnpike - {plazaType} - Class {priceInfo.TollClass} ({priceInfo.VehicleType})"));
                            }

                            // Добавляем WV E-ZPass Commercial Rate
                            if (priceInfo.WVEZPassCommercialRate > 0)
                            {
                                tollPricesByTollId[toll.Id].Add(new TollPriceData(
                                    TollId: toll.Id,
                                    Amount: priceInfo.WVEZPassCommercialRate,
                                    PaymentType: TollPaymentType.EZPass,
                                    AxelType: axelType,
                                    Description: $"West Virginia Turnpike - {plazaType} - Class {priceInfo.TollClass} ({priceInfo.VehicleType}) - WV E-ZPass Commercial"));
                            }

                            // Добавляем Non-WV E-ZPass Rate
                            if (priceInfo.NonWVEZPassRate > 0)
                            {
                                tollPricesByTollId[toll.Id].Add(new TollPriceData(
                                    TollId: toll.Id,
                                    Amount: priceInfo.NonWVEZPassRate,
                                    PaymentType: TollPaymentType.EZPass,
                                    AxelType: axelType,
                                    Description: $"West Virginia Turnpike - {plazaType} - Class {priceInfo.TollClass} ({priceInfo.VehicleType}) - Non-WV E-ZPass"));
                            }

                            // Добавляем Pay-by-Plate Rate (если есть)
                            if (priceInfo.PayByPlateRate.HasValue && priceInfo.PayByPlateRate.Value > 0)
                            {
                                tollPricesByTollId[toll.Id].Add(new TollPriceData(
                                    TollId: toll.Id,
                                    Amount: priceInfo.PayByPlateRate.Value,
                                    PaymentType: TollPaymentType.PayOnline,
                                    AxelType: axelType,
                                    Description: $"West Virginia Turnpike - {plazaType} - Class {priceInfo.TollClass} ({priceInfo.VehicleType}) - Pay-by-Plate"));
                            }

                            // Добавляем информацию о связанном toll
                            linkedTolls.Add(new WestVirginiaLinkedTollInfo(
                                PlazaType: plazaType,
                                TollId: toll.Id,
                                TollName: toll.Name,
                                TollKey: toll.Key,
                                TollClass: priceInfo.TollClass,
                                Axles: priceInfo.Axles));
                        }
                    }
                }

                // Устанавливаем цены используя новый метод
                if (tollPricesByTollId.Count > 0)
                {
                    var tollPricesByTollIdEnumerable = tollPricesByTollId.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (IEnumerable<TollPriceData>)kvp.Value);

                    var result = await _calculatePriceService.SetTollPricesDirectlyBatchAsync(
                        tollPricesByTollIdEnumerable,
                        null,
                        ct);

                    updatedTollsCount = result.Count;
                }

                await _context.SaveChangesAsync(ct);
            }

            return new ParseWestVirginiaTollPricesResult(
                prices,
                linkedTolls,
                notFoundPlazas.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new ParseWestVirginiaTollPricesResult(
                new List<WestVirginiaTollPriceInfo>(),
                new List<WestVirginiaLinkedTollInfo>(),
                new List<string>(),
                0,
                $"Ошибка при парсинге: {ex.Message}");
        }
    }

    private static string ExtractText(HtmlNode? node)
    {
        if (node == null)
            return string.Empty;

        // Используем InnerText, который автоматически декодирует HTML entities
        var text = node.InnerText?.Trim() ?? string.Empty;
        // Убираем лишние пробелы и переносы строк
        text = Regex.Replace(text, @"\s+", " ");
        return text;
    }

    private static double ParsePrice(HtmlNode? node)
    {
        if (node == null)
            return 0;

        var text = ExtractText(node);
        // Убираем символ доллара
        text = text.Replace("$", "").Trim();

        // Убираем все нецифровые символы кроме точки и запятой
        text = Regex.Replace(text, @"[^\d.,]", "").Trim();

        // Заменяем запятую на точку для парсинга
        text = text.Replace(",", ".");

        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var price))
        {
            return price;
        }

        return 0;
    }

    private static int ParseAxles(string axlesText)
    {
        // "5" -> 5, "6+" -> 6
        var match = Regex.Match(axlesText, @"(\d+)");
        if (match.Success && int.TryParse(match.Value, out var axles))
        {
            return axles;
        }
        return 0;
    }
}

