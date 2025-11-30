using System.Globalization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NY;

/// <summary>
/// Парсер тарифов для New York State Thruway.
/// Делает запросы ко всем парам въезд/выезд (n^2) для существующих точек Toll в штате Нью‑Йорк
/// на сайт калькулятора:
/// https://tollcalculator.thruway.ny.gov/index.aspx?Class=7&amp;Entry=m04x&amp;Exit=m37x
/// и вытаскивает из HTML только строку Total (NY E‑ZPass и NON‑NY E‑ZPass &amp; Tolls By Mail).
/// </summary>
public record ParseNewYorkTollPricesCommand(
    int VehicleClass = 5,
    string BaseUrl = "https://tollcalculator.thruway.ny.gov/index.aspx")
    : IRequest<ParseTollPricesResult>;

public class ParseNewYorkTollPricesCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory)
    : IRequestHandler<ParseNewYorkTollPricesCommand, ParseTollPricesResult>
{
    // Границы Нью-Йорка (как в OsmImportService)
    private const double NyMinLatitude = 40.5;
    private const double NyMaxLatitude = 45.0;
    private const double NyMinLongitude = -79.8;
    private const double NyMaxLongitude = -71.8;

    public async Task<ParseTollPricesResult> Handle(
        ParseNewYorkTollPricesCommand request,
        CancellationToken ct)
    {
        var notFoundPairs = new List<string>();
        int updatedCount = 0;

        // 1. Получаем или создаем StateCalculator для NY
        var nyCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "NY", ct);

        if (nyCalculator == null)
        {
            nyCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "New York State Thruway",
                StateCode = "NY"
            };
            _context.StateCalculators.Add(nyCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // 2. Находим все Toll в пределах штата Нью-Йорк, у которых есть Number
        var nyTolls = await _context.Tolls
            .Where(t =>
                t.Location != null &&
                t.Location.Y >= NyMinLatitude && t.Location.Y <= NyMaxLatitude &&
                t.Location.X >= NyMinLongitude && t.Location.X <= NyMaxLongitude &&
                t.Number != null && t.Number != string.Empty)
            .ToListAsync(ct);

        if (nyTolls.Count == 0)
        {
            return new ParseTollPricesResult(0, new List<string> { "В БД не найдено ни одного Toll для штата NY с Number" });
        }

        // Сгруппируем по Number, чтобы сделать по одному HTTP‑запросу на комбинацию номеров,
        // а потом размножить цену на все Toll с такими номерами
        var tollsByNumber = nyTolls
            .GroupBy(t => t.Number!.Trim())
            .ToDictionary(g => g.Key, g => g.ToList());

        var numbers = tollsByNumber.Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (numbers.Count == 0)
        {
            return new ParseTollPricesResult(0, new List<string> { "Не удалось получить список номеров плаз для NY" });
        }

        // 3. Загружаем уже существующие цены для этого калькулятора
        var existingPrices = await _context.CalculatePrices
            .Where(cp => cp.StateCalculatorId == nyCalculator.Id)
            .ToListAsync(ct);

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Чтобы не дергать один и тот же URL дважды
        var processedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 4. Полный перебор всех пар (entry, exit)
        foreach (var entryNumber in numbers)
        {
            foreach (var exitNumber in numbers)
            {
                ct.ThrowIfCancellationRequested();

                if (string.Equals(entryNumber, exitNumber, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pairKey = $"{entryNumber}->{exitNumber}";
                if (!processedPairs.Add(pairKey))
                    continue;

                var entryCode = BuildThruwayCode(entryNumber);
                var exitCode = BuildThruwayCode(exitNumber);

                if (entryCode == null || exitCode == null)
                {
                    notFoundPairs.Add($"{pairKey}: cannot map numbers to Entry/Exit codes");
                    continue;
                }

                var url = $"{request.BaseUrl}?Class={request.VehicleClass}&Entry={entryCode}&Exit={exitCode}";

                string html;
                try
                {
                    html = await httpClient.GetStringAsync(url, ct);
                }
                catch (Exception ex)
                {
                    notFoundPairs.Add($"{pairKey}: HTTP error: {ex.Message}");
                    continue;
                }

                if (!TryParseTotals(html, out var ezPass, out var mailRate))
                {
                    notFoundPairs.Add($"{pairKey}: cannot parse Total row");
                    continue;
                }

                var fromTolls = tollsByNumber[entryNumber];
                var toTolls = tollsByNumber[exitNumber];

                foreach (var fromToll in fromTolls)
                {
                    foreach (var toToll in toTolls)
                    {
                        if (fromToll.Id == toToll.Id)
                            continue;

                        var existingPrice = existingPrices
                            .FirstOrDefault(cp =>
                                cp.FromId == fromToll.Id &&
                                cp.ToId == toToll.Id);

                        if (existingPrice != null)
                        {
                            existingPrice.IPass = (double)ezPass;       // NY E‑ZPass
                            existingPrice.Cash = (double)mailRate;      // NON‑NY E‑ZPass & Tolls By Mail
                            existingPrice.Online = (double)mailRate;    // Online = Mail
                        }
                        else
                        {
                            var calculatePrice = new CalculatePrice
                            {
                                Id = Guid.NewGuid(),
                                StateCalculatorId = nyCalculator.Id,
                                FromId = fromToll.Id,
                                ToId = toToll.Id,
                                IPass = (double)ezPass,
                                Cash = (double)mailRate,
                                Online = (double)mailRate
                            };
                            _context.CalculatePrices.Add(calculatePrice);
                            existingPrices.Add(calculatePrice);
                        }

                        updatedCount++;
                    }
                }
            }
        }

        if (updatedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new ParseTollPricesResult(
            updatedCount,
            notFoundPairs.Distinct().ToList());
    }

    /// <summary>
    /// Маппит Number в формат Entry/Exit для калькулятора:
    /// "4"  -> "m4x"
    /// "4a" -> "m4a"
    /// "37" -> "m37x"
    /// </summary>
    private static string? BuildThruwayCode(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return null;

        var text = number.Trim().ToLowerInvariant();
        var match = Regex.Match(text, @"^(\d+)([a-z])?$");
        if (!match.Success)
            return null;

        var digits = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        var letter = match.Groups[2].Success ? match.Groups[2].Value : "x";

        return $"m{digits}{letter}";
    }

    /// <summary>
    /// Ищет в HTML строку Total и достает два значения: NY E‑ZPass и NON‑NY E‑ZPass &amp; Tolls By Mail.
    /// Опирается на структуру страницы калькулятора:
    /// https://tollcalculator.thruway.ny.gov/index.aspx?Class=7&amp;Entry=m04x&amp;Exit=m37x
    /// </summary>
    private static bool TryParseTotals(string html, out decimal ezPass, out decimal mailRate)
    {
        ezPass = 0m;
        mailRate = 0m;

        if (string.IsNullOrWhiteSpace(html))
            return false;

        var idx = html.IndexOf(">Total<", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        var length = Math.Min(html.Length - idx, 1200);
        var snippet = html.Substring(idx, length);

        // Ищем все суммы вида $78.84
        var matches = Regex.Matches(snippet, @"\$(\d+(\.\d+)?)", RegexOptions.IgnoreCase);
        if (matches.Count < 2)
            return false;

        if (!decimal.TryParse(matches[0].Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out ezPass))
            return false;

        if (!decimal.TryParse(matches[1].Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out mailRate))
            return false;

        return true;
    }
}



