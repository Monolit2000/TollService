using MediatR;
using Microsoft.EntityFrameworkCore;
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
        // Загружаем HTML страницу
        var httpClient = _httpClientFactory.CreateClient();
        var html = await httpClient.GetStringAsync(request.Url, ct);
        
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

