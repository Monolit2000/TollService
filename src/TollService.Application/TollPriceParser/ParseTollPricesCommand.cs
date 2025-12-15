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
        // Проверяем, является ли URL JS файлом Ohio Turnpike
        if (request.Url.Contains("ohioturnpike.org") && request.Url.Contains(".js"))
        {
            return await ParseOhioTurnpikeJs(request.Url, ct);
        }
        
        // Оригинальный парсер для Illinois Tollway
        var httpClient = _httpClientFactory.CreateClient();
        return await IllinoisTollwayPriceParser.ParseAsync(request.Url, _context, httpClient, ct);
    }
    
    private async Task<ParseTollPricesResult> ParseOhioTurnpikeJs(string url, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var jsContent = await httpClient.GetStringAsync("https://www.ohioturnpike.org/js/rate_calculator_2025_combined.js?Modified=3426893245967234", ct);
        
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;
        
        // Парсим interchangesrate массив
        var interchanges = ParseInterchanges(jsContent);
        
        // Парсим tolls[entrance][exit][5][3] записи
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
 

            var tolls = ohioTolls
                .Where(t => t.Location != null
                            && t.Name != null
                            && t.Name == interchange.Name)
                .ToList();

            Toll? toll = null;
            
            if(tolls.Count < 0)
                Console.WriteLine(interchange.Name);

            if (tolls.Count > 0)
            {
                // Обрабатываем все найденные tolls
                foreach (var existingToll in tolls)
                {
                    // Обновляем информацию о развязке для каждого toll
                    existingToll.Number = interchange.Milepost.ToString();
                    existingToll.Key = interchange.Name;
                    //if (string.IsNullOrWhiteSpace(existingToll.Name))
                    //{
                    //    existingToll.Name = interchange.Name;
                    //}
                    existingToll.StateCalculatorId = ohioCalculator.Id;
                }
                
                // Используем первый для карты (для обратной совместимости с логикой CalculatePrice)
                toll = tolls.First();
            }
            //else
            //{
            //    // Создаем новую запись Toll
            //    toll = new Toll
            //    {
            //        Id = Guid.NewGuid(),
            //        Name = interchange.Name,
            //        Number = interchange.Milepost,
            //        Key = interchange.Name,
            //        StateCalculatorId = ohioCalculator.Id
            //    };
            //    _context.Tolls.Add(toll);
            //    await _context.SaveChangesAsync(ct);
            //}
            if(toll != null)
                milepostToTollMap[interchange.Milepost] = toll;
        }

        var tollsId = ohioTolls.Select(t => t.Id);

        var existingPrices = await _context.CalculatePrices
            .Where(cp =>
                tollsId.Contains(cp.FromId) ||
                tollsId.Contains(cp.ToId) &&
                cp.StateCalculatorId == ohioCalculator.Id).ToListAsync(ct);

        // Создаем или обновляем CalculatePrice записи
        foreach (var priceEntry in tollPrices)
        {

            if(priceEntry.EntranceMilepost == 0)
            {

            }

            if (!milepostToTollMap.TryGetValue(priceEntry.EntranceMilepost, out var fromToll) ||
                !milepostToTollMap.TryGetValue(priceEntry.ExitMilepost, out var toToll))
            {
                notFoundPlazas.Add($"Milepost {priceEntry.EntranceMilepost} -> {priceEntry.ExitMilepost}");
                continue;
            }


            
            // Проверяем, существует ли уже CalculatePrice для этой пары
            var existingPrice = existingPrices
                .FirstOrDefault(cp => 
                    cp.FromId == fromToll.Id && 
                    cp.ToId == toToll.Id && 
                    cp.StateCalculatorId == ohioCalculator.Id);
            
            if (existingPrice != null)
            {
                // Обновляем существующую запись
                existingPrice.Online = priceEntry.Unpaid; // tollindex 3 = upt (online)
                existingPrice.IPass = priceEntry.EZpass; // tollindex 2 = ezpass
                existingPrice.Cash = priceEntry.Unpaid;
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
                    Online = priceEntry.Unpaid,
                    IPass = priceEntry.EZpass,
                    Cash = priceEntry.Unpaid
                };
                _context.CalculatePrices.Add(calculatePrice);
            }
            
            updatedCount++;
        }
        
        await _context.SaveChangesAsync(ct);
        
        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
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
        var pricesDict = new Dictionary<(int entrance, int exit), TollPriceEntry>();
        
        // Парсим индекс 3 (Unpaid)
        var patternUnpaid = @"tolls\[(\d+)\]\[(\d+)\]\[5\]\[3\]\s*=\s*([\d.]+)\s*;?";
        var matchesUnpaid = Regex.Matches(jsContent, patternUnpaid);
        
        foreach (Match match in matchesUnpaid)
        {
            if (match.Groups.Count >= 4)
            {
                if (int.TryParse(match.Groups[1].Value, out var entrance) &&
                    int.TryParse(match.Groups[2].Value, out var exit) &&
                    double.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    var key = (entrance, exit);
                    if (!pricesDict.ContainsKey(key))
                    {
                        pricesDict[key] = new TollPriceEntry
                        {
                            EntranceMilepost = entrance,
                            ExitMilepost = exit
                        };
                    }
                    pricesDict[key].Unpaid = price;
                }
            }
        }
        
        // Парсим индекс 2 (EZpass)
        var patternEZpass = @"tolls\[(\d+)\]\[(\d+)\]\[5\]\[2\]\s*=\s*([\d.]+)\s*;?";
        var matchesEZpass = Regex.Matches(jsContent, patternEZpass);
        
        foreach (Match match in matchesEZpass)
        {
            if (match.Groups.Count >= 4)
            {
                if (int.TryParse(match.Groups[1].Value, out var entrance) &&
                    int.TryParse(match.Groups[2].Value, out var exit) &&
                    double.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    var key = (entrance, exit);
                    if (!pricesDict.ContainsKey(key))
                    {
                        pricesDict[key] = new TollPriceEntry
                        {
                            EntranceMilepost = entrance,
                            ExitMilepost = exit
                        };
                    }
                    pricesDict[key].EZpass = price;
                }
            }
        }
        
        return pricesDict.Values.ToList();
    }
    
    // Illinois Tollway parsing logic moved to `TollPriceParser/IL/IllinoisTollwayPriceParser.cs`
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
    public double Unpaid { get; set; } //inex 3
    public double EZpass { get; set; } //index 2
}

