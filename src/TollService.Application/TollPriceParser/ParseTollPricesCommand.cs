using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TollService.Application.Common.Interfaces;
using TollService.Application.TollPriceParser.IL;
using TollService.Domain;

namespace TollService.Application.TollPriceParser;

public record ParseTollPricesCommand(string Url = "https://agency.illinoistollway.com/toll-rates") 
    : IRequest<ParseTollPricesResult>;


public record ParseTollPricesResult(
    int UpdatedCount,
    List<string> NotFoundPlazas);

public class ParseTollPricesCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory) : IRequestHandler<ParseTollPricesCommand, ParseTollPricesResult>
{
    private static readonly (double south, double west, double north, double east) OhioBounds =
    (38.4, -84.8, 42.0, -80.5);

    public async Task<ParseTollPricesResult> Handle(ParseTollPricesCommand request, CancellationToken ct)
    {
        // Ohio Turnpike (JS rate calculator)
        //if (LooksLikeOhioTurnpikeJs(request.Url))
        //{
            return await ParseOhioTurnpikeJs(request.Url, ct);
        //}

        // Illinois Tollway (original parser)
        //var httpClient = _httpClientFactory.CreateClient();
        //return await IllinoisTollwayPriceParser.ParseAsync(request.Url, _context, httpClient, ct);
    }
    
    private async Task<ParseTollPricesResult> ParseOhioTurnpikeJs(string url, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var jsUrl = string.IsNullOrWhiteSpace(url)
            ? "https://www.ohioturnpike.org/js/rate_calculator_2025_combined.js"
            : url;
        var jsContent = await httpClient.GetStringAsync(jsUrl, ct);
        
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;
        
        // Парсим interchangesrate массив
        var interchanges = ParseInterchanges(jsContent);
        
        // Парсим tolls[entrance][exit][axleClass][tollIndex] записи (axleClass обычно 5/6, tollIndex 2/3)
        var tollPrices = ParseTollPrices(jsContent);
        
        // Получаем или создаем StateCalculator для Ohio
        var ohioCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "OH", ct);
        
        if (ohioCalculator == null)
        {
            ohioCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "Ohio Turnpike",
                StateCode = "OH"
            };
            _context.StateCalculators.Add(ohioCalculator);
            await _context.SaveChangesAsync(ct);
        }

        var (south, west, north, east) = OhioBounds;

        var ohioTolls = await _context.Tolls
    .Where(t => t.Location != null
                && t.Location.Y >= south && t.Location.Y <= north   // latitude
                && t.Location.X >= west && t.Location.X <= east)   // longitude
    .ToListAsync(ct);

        // Обновляем или создаем Toll записи на основе interchanges
        var milepostToTollMap = new Dictionary<int, Toll>();
        
        foreach (var interchange in interchanges)
        {
            // Пытаемся найти toll по Name или по уже выставленному Number (milepost)
            var normalizedName = interchange.Name?.Trim();
            var milepostStr = interchange.Milepost.ToString();

            var tolls = ohioTolls
                .Where(t =>
                    t.Location != null &&
                    (
                        (t.Name != null && normalizedName != null && t.Name.Trim() == normalizedName) ||
                        (t.Number != null && t.Number.Trim() == milepostStr)
                    ))
                .ToList();

            Toll? toll = null;
            
            if (tolls.Count > 0)
            {
                // Обрабатываем все найденные tolls
                foreach (var existingToll in tolls)
                {
                    // Обновляем информацию о развязке для каждого toll
                    existingToll.Number = interchange.Milepost.ToString();
                    existingToll.Key = interchange.Name;
                    if (string.IsNullOrWhiteSpace(existingToll.Name))
                    {
                        existingToll.Name = interchange.Name;
                    }
                    existingToll.StateCalculatorId = ohioCalculator.Id;
                }
                
                // Используем первый для карты (для обратной совместимости с логикой CalculatePrice)
                toll = tolls.First();
            }
            else
            {
                // В рамках этого парсера мы НЕ создаем новые Toll без координат, чтобы не засорять БД.
                notFoundPlazas.Add($"Interchange toll not found in DB: {interchange.Milepost} ({interchange.Name})");
            }

            if (toll != null)
                milepostToTollMap[interchange.Milepost] = toll;
        }

        var existingPrices = await _context.CalculatePrices
            .Where(cp => cp.StateCalculatorId == ohioCalculator.Id)
            .Include(cp => cp.TollPrices)
            .ToListAsync(ct);

        // Создаем или обновляем CalculatePrice записи
        foreach (var priceEntry in tollPrices)
        {
            if (!milepostToTollMap.TryGetValue(priceEntry.EntranceMilepost, out var fromToll) ||
                !milepostToTollMap.TryGetValue(priceEntry.ExitMilepost, out var toToll))
            {
                notFoundPlazas.Add($"Milepost {priceEntry.EntranceMilepost} -> {priceEntry.ExitMilepost}");
                continue;
            }

            // Маппим axleClass (из JS) в AxelType
            if (!Enum.IsDefined(typeof(AxelType), priceEntry.AxleClass))
            {
                // Неизвестный класс — пропускаем
                continue;
            }
            var axelType = (AxelType)priceEntry.AxleClass;

            // Проверяем, существует ли уже CalculatePrice для этой пары (без учета осей — оси идут в TollPrices)
            var existingPrice = existingPrices
                .FirstOrDefault(cp =>
                    cp.FromId == fromToll.Id &&
                    cp.ToId == toToll.Id);
            
            if (existingPrice != null)
            {
                // Upsert детализацию по осям/методу оплаты
                UpsertTollPrice(existingPrice, fromToll.Id, priceEntry.EZpass, TollPaymentType.EZPass, axelType);
                UpsertTollPrice(existingPrice, fromToll.Id, priceEntry.Unpaid, TollPaymentType.PayOnline, axelType);
                // В базе/контрактах часто используется Cash как "не транспондер", поэтому дублируем сюда Unpaid.
                UpsertTollPrice(existingPrice, fromToll.Id, priceEntry.Unpaid, TollPaymentType.Cash, axelType);

                // Поля CalculatePrice не поддерживают "по осям" — заполняем их по умолчанию классом 5,
                // чтобы не ломать существующие сценарии чтения.
                if (axelType == AxelType._5L)
                {
                    existingPrice.Online = priceEntry.Unpaid;
                    existingPrice.IPass = priceEntry.EZpass;
                    existingPrice.Cash = priceEntry.Unpaid;
                }
            }
            else
            {
                // Создаем новую запись
                var calculatePrice = new CalculatePrice
                {
                    Id = Guid.NewGuid(),
                    StateCalculatorId = ohioCalculator.Id,
                    FromId = fromToll.Id,
                    ToId = toToll.Id,
                    Online = axelType == AxelType._5L ? priceEntry.Unpaid : 0,
                    // Эти поля считаем "дефолтными" (под класс 5); реальные цены лежат в TollPrices
                    IPass = axelType == AxelType._5L ? priceEntry.EZpass : 0,
                    Cash = axelType == AxelType._5L ? priceEntry.Unpaid : 0,
                    TollPrices = []
                };
                calculatePrice.TollPrices.Add(CreateTollPrice(
                    calculatePrice.Id,
                    fromToll.Id,
                    priceEntry.EZpass,
                    TollPaymentType.EZPass,
                    axelType));

                calculatePrice.TollPrices.Add(CreateTollPrice(
                    calculatePrice.Id,
                    fromToll.Id,
                    priceEntry.Unpaid,
                    TollPaymentType.PayOnline,
                    axelType));

                calculatePrice.TollPrices.Add(CreateTollPrice(
                    calculatePrice.Id,
                    fromToll.Id,
                    priceEntry.Unpaid,
                    TollPaymentType.Cash,
                    axelType));

                _context.CalculatePrices.Add(calculatePrice);
                existingPrices.Add(calculatePrice);
            }
            
            updatedCount++;
        }
        
        await _context.SaveChangesAsync(ct);
        
        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
    }

    private static bool LooksLikeOhioTurnpikeJs(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var u = url.Trim();
        return u.Contains("ohioturnpike.org", StringComparison.OrdinalIgnoreCase) ||
               u.Contains("rate_calculator", StringComparison.OrdinalIgnoreCase) ||
               u.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
    }
    
    private List<InterchangeInfo> ParseInterchanges(string jsContent)
    {
        var interchanges = new List<InterchangeInfo>();
        
        // Ищем паттерн: interchangesrate[index] = { name: '...', milepost: ..., showOnDropdown: ... };
        // Поддерживаем оба типа кавычек и различный порядок свойств
        var pattern = @"interchangesrate\[\d+\]\s*=\s*\{[^}]*name:\s*['""]([^'""]+)['""][^}]*milepost:\s*(\d+)";
        var matches = Regex.Matches(jsContent, pattern, RegexOptions.IgnoreCase);
        
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 3)
            {
                var name = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out var milepost))
                {
                    interchanges.Add(new InterchangeInfo
                    {
                        Name = name,
                        Milepost = milepost
                    });
                }
            }
        }
        
        return interchanges;
    }
    
    private List<TollPriceEntry> ParseTollPrices(string jsContent)
    {
        var pricesDict = new Dictionary<(int entrance, int exit, int axleClass), TollPriceEntry>();
        
        // Парсим индекс 3 (Unpaid / Pay Online) для всех классов осей
        var patternUnpaid = @"tolls\[(\d+)\]\[(\d+)\]\[(\d+)\]\[3\]\s*=\s*([\d.]+)\s*;?";
        var matchesUnpaid = Regex.Matches(jsContent, patternUnpaid);
        
        foreach (Match match in matchesUnpaid)
        {
            if (match.Groups.Count >= 5)
            {
                if (int.TryParse(match.Groups[1].Value, out var entrance) &&
                    int.TryParse(match.Groups[2].Value, out var exit) &&
                    int.TryParse(match.Groups[3].Value, out var axleClass) &&
                    double.TryParse(match.Groups[4].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    var key = (entrance, exit, axleClass);
                    if (!pricesDict.ContainsKey(key))
                    {
                        pricesDict[key] = new TollPriceEntry
                        {
                            EntranceMilepost = entrance,
                            ExitMilepost = exit,
                            AxleClass = axleClass
                        };
                    }
                    pricesDict[key].Unpaid = price;
                }
            }
        }
        
        // Парсим индекс 2 (EZPass) для всех классов осей
        var patternEZpass = @"tolls\[(\d+)\]\[(\d+)\]\[(\d+)\]\[2\]\s*=\s*([\d.]+)\s*;?";
        var matchesEZpass = Regex.Matches(jsContent, patternEZpass);
        
        foreach (Match match in matchesEZpass)
        {
            if (match.Groups.Count >= 5)
            {
                if (int.TryParse(match.Groups[1].Value, out var entrance) &&
                    int.TryParse(match.Groups[2].Value, out var exit) &&
                    int.TryParse(match.Groups[3].Value, out var axleClass) &&
                    double.TryParse(match.Groups[4].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    var key = (entrance, exit, axleClass);
                    if (!pricesDict.ContainsKey(key))
                    {
                        pricesDict[key] = new TollPriceEntry
                        {
                            EntranceMilepost = entrance,
                            ExitMilepost = exit,
                            AxleClass = axleClass
                        };
                    }
                    pricesDict[key].EZpass = price;
                }
            }
        }
        
        return pricesDict.Values.ToList();
    }
    
    // Illinois Tollway parsing logic moved to `TollPriceParser/IL/IllinoisTollwayPriceParser.cs`

    private static void UpsertTollPrice(
        CalculatePrice calculatePrice,
        Guid tollId,
        double amount,
        TollPaymentType paymentType,
        AxelType axelType)
    {
        calculatePrice.TollPrices ??= [];

        var tollPrice = calculatePrice.TollPrices.FirstOrDefault(tp =>
            tp.PaymentType == paymentType &&
            tp.AxelType == axelType);

        if (tollPrice != null)
        {
            tollPrice.Amount = amount;
            tollPrice.TollId = tollId;
        }
        else
        {
            calculatePrice.TollPrices.Add(CreateTollPrice(
                calculatePrice.Id,
                tollId,
                amount,
                paymentType,
                axelType));
        }
    }

    private static TollPrice CreateTollPrice(
        Guid calculatePriceId,
        Guid tollId,
        double amount,
        TollPaymentType paymentType,
        AxelType axelType)
    {
        var tollPrice = new TollPrice(
            calculatePriceId: calculatePriceId,
            amount: amount,
            paymentType: paymentType,
            axelType: axelType);
        tollPrice.TollId = tollId;
        return tollPrice;
    }
}

// Вспомогательные классы для парсинга Ohio Turnpike
internal class InterchangeInfo
{
    public string Name { get; set; } = string.Empty;
    public int Milepost { get; set; }
}

internal class TollPriceEntry
{
    public int EntranceMilepost { get; set; }
    public int ExitMilepost { get; set; }
    public int AxleClass { get; set; }
    public double Unpaid { get; set; } //inex 3
    public double EZpass { get; set; } //index 2
}

