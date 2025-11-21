using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TollService.Application.Common.Interfaces;
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
    public async Task<ParseTollPricesResult> Handle(ParseTollPricesCommand request, CancellationToken ct)
    {
        // Проверяем, является ли URL JS файлом Ohio Turnpike
        if (request.Url.Contains("ohioturnpike.org") && request.Url.Contains(".js"))
        {
            return await ParseOhioTurnpikeJs(request.Url, ct);
        }
        
        // Оригинальный парсер для Illinois Tollway
        return await ParseIllinoisTollway(request.Url, ct);
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
        
        // Обновляем или создаем Toll записи на основе interchanges
        var milepostToTollMap = new Dictionary<int, Toll>();
        
        foreach (var interchange in interchanges)
        {
            var tolls = await _context.Tolls
                .Where(t => /*t.Number == interchange.Milepost || */
                           //(t.Key != null && t.Key.Equals(interchange.Name, StringComparison.OrdinalIgnoreCase)) ||
                           (t.Name != null && t.Name == interchange.Name))
                           //(t.Name != null && EF.Functions.Like(t.Name, $"%{interchange.Name}%")))
                .ToListAsync(ct);
            
            Toll? toll = null;
            
            if(tolls.Count < 0)
                Console.WriteLine(interchange.Name);

            if (tolls.Count > 0)
            {
                // Обрабатываем все найденные tolls
                foreach (var existingToll in tolls)
                {
                    // Обновляем информацию о развязке для каждого toll
                    existingToll.Number = interchange.Milepost;
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
        
        // Создаем или обновляем CalculatePrice записи
        foreach (var priceEntry in tollPrices)
        {
            if (!milepostToTollMap.TryGetValue(priceEntry.EntranceMilepost, out var fromToll) ||
                !milepostToTollMap.TryGetValue(priceEntry.ExitMilepost, out var toToll))
            {
                notFoundPlazas.Add($"Milepost {priceEntry.EntranceMilepost} -> {priceEntry.ExitMilepost}");
                continue;
            }
            
            // Проверяем, существует ли уже CalculatePrice для этой пары
            var existingPrice = await _context.CalculatePrices
                .FirstOrDefaultAsync(cp => 
                    cp.FromId == fromToll.Id && 
                    cp.ToId == toToll.Id && 
                    cp.StateCalculatorId == ohioCalculator.Id, ct);
            
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
    
    private async Task<ParseTollPricesResult> ParseIllinoisTollway(string url, CancellationToken ct)
    {
        // Загружаем HTML страницу
        var httpClient = _httpClientFactory.CreateClient();
        var html = await httpClient.GetStringAsync(url, ct);
        
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);
        
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;
        
        // Находим все таблицы с ценами
        var tables = doc.DocumentNode.SelectNodes("//table[@class='table table-istha table-striped table-condensed']");
        
        if (tables == null)
        {
            return new ParseTollPricesResult(0, new List<string> { "Таблицы не найдены на странице" });
        }
        
        foreach (var table in tables)
        {
            var rows = table.SelectNodes(".//tbody/tr");
            if (rows == null) continue;
            
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 10) continue;
                
                // Извлекаем данные из строки таблицы
                var plazaName = ExtractPlazaName(cells[0]);
                if (string.IsNullOrWhiteSpace(plazaName)) continue;
                
                // Пропускаем строки с "I-PASS" или "Pay Online" (это подзаголовки для Route 390)
                if (plazaName.Equals("I-PASS", StringComparison.OrdinalIgnoreCase) ||
                    plazaName.Equals("Pay Online", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Извлекаем номер плазы
                var plazaNumber = ParsePlazaNumber(cells[1]);
                
                // Извлекаем цены
                // Структура таблицы:
                // [0] - Toll Plaza Name
                // [1] - Plaza Number
                // [2] - I-PASS (для авто)
                // [3] - Pay Online (для авто)
                // [4] - Daytime Small
                // [5] - Daytime Medium
                // [6] - Daytime Large
                // [7] - Overnight Small
                // [8] - Overnight Medium
                // [9] - Overnight Large
                
                var iPass = ParsePrice(cells[2]);
                var payOnline = ParsePrice(cells[3]);
                var largeOvernight = ParsePrice(cells.Count > 9 ? cells[9] : null); // Overnight Large
                var largeDaytime = ParsePrice(cells.Count > 6 ? cells[6] : null); // Daytime Large

                // Ищем соответствие в БД
                var tolls = await _context.Tolls
                    .Where(t => t.Number == plazaNumber)  // case-insensitive by default in most DBs
                    .ToListAsync(ct);

                if (tolls.Count == 0)
                {
                    notFoundPlazas.Add(plazaName);
                    continue;
                }
                
                // Обновляем найденные записи
                foreach (var toll in tolls)
                {
                    toll.IPass = iPass;
                    toll.PayOnline = payOnline;
                    toll.IPassOvernight = largeOvernight; // Large Overnight для грузовиков
                    toll.PayOnlineOvernight = largeDaytime; // Large Daytime для грузовиков
                    toll.Key = plazaName;
                    toll.Number = plazaNumber;
                    updatedCount++;
                }
            }
        }
        
        await _context.SaveChangesAsync(ct);
        
        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
    }
    
    private static string ExtractPlazaName(HtmlAgilityPack.HtmlNode cell)
    {
        var text = cell.InnerText?.Trim() ?? string.Empty;
        // Убираем HTML теги и лишние пробелы
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        // Убираем скобки и всё, что внутри них
        //text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\([^)]*\)", "");
        // Убираем дефисы и запятые
        //text = text.Replace("-", "").Replace(",", "");
        // Убираем звездочку в конце (если есть)
        text = text.TrimEnd('*');
        return text.Trim();
    }
    
    private static double ParsePrice(HtmlAgilityPack.HtmlNode? cell)
    {
        if (cell == null) return 0;
        
        var text = cell.InnerText?.Trim() ?? string.Empty;
        // Убираем символ доллара и пробелы
        text = text.Replace("$", "").Trim();
        
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, 
            System.Globalization.CultureInfo.InvariantCulture, out var price))
        {
            return price;
        }
        
        return 0;
    }
    
    private static int ParsePlazaNumber(HtmlAgilityPack.HtmlNode? cell)
    {
        if (cell == null) return 0;
        
        var text = cell.InnerText?.Trim() ?? string.Empty;
        
        // Извлекаем числовую часть (например, "5A" -> "5", "300" -> "300")
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var number))
        {
            return number;
        }
        
        return 0;
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
    public double Unpaid { get; set; } //inex 3
    public double EZpass { get; set; } //index 2
}

