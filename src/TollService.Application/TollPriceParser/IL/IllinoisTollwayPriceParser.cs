using TollService.Application.Common.Interfaces;
using TollService.Application.TollPriceParser;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;

namespace TollService.Application.TollPriceParser.IL;

internal static class IllinoisTollwayPriceParser
{
    internal static async Task<ParseTollPricesResult> ParseAsync(
        string url,
        ITollDbContext context,
        HttpClient httpClient,
        CancellationToken ct)
    {
        // Загружаем HTML страницу
        var html = await httpClient.GetStringAsync(url, ct);

        var doc = new HtmlDocument();
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
                var plazaNumber = ParsePlazaNumber(cells[1]).ToString();

                // Извлекаем цены
                var _ = ParsePrice(cells[2]); // iPass (currently unused)
                _ = ParsePrice(cells[3]); // payOnline (currently unused)
                var largeOvernight = ParsePrice(cells.Count > 9 ? cells[9] : null); // Overnight Large
                var largeDaytime = ParsePrice(cells.Count > 6 ? cells[6] : null); // Daytime Large

                var cleanedPlazaName = plazaName
                    .Replace("*&nbsp;&nbsp;", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("&nbsp", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("&nbs", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("*", "")
                    .Trim();

                // Ищем соответствие в БД
                var tolls = await context.Tolls
                    .Where(t => t.Key == cleanedPlazaName)
                    .ToListAsync(ct);

                if (tolls.Count == 0)
                {
                    notFoundPlazas.Add(plazaName);
                    continue;
                }

                // Обновляем найденные записи
                foreach (var toll in tolls)
                {
                    toll.IPass = 0;
                    toll.PayOnline = largeDaytime;
                    toll.IPassOvernight = 0; // Large Overnight для грузовиков
                    toll.PayOnlineOvernight = largeOvernight; // Large Daytime для грузовиков
                    toll.Key = plazaName;
                    toll.Number = plazaNumber;
                    updatedCount++;
                }
            }
        }

        await context.SaveChangesAsync(ct);

        return new ParseTollPricesResult(updatedCount, notFoundPlazas.Distinct().ToList());
    }

    private static string ExtractPlazaName(HtmlNode cell)
    {
        var text = cell.InnerText?.Trim() ?? string.Empty;
        // Убираем HTML теги и лишние пробелы
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        // Убираем звездочку в конце (если есть)
        text = text.TrimEnd('*');
        return text.Trim();
    }

    private static double ParsePrice(HtmlNode? cell)
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

    private static int ParsePlazaNumber(HtmlNode? cell)
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


