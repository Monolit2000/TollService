using System.Globalization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Domain;
using static TollService.Application.TollPriceParser.NY.ParseNewYorkTollPricesCommandHandler;

namespace TollService.Application.TollPriceParser.NY;

/// <summary>
/// Парсер тарифов для New York State Thruway.
/// Делает запросы ко всем парам въезд/выезд (n^2) для существующих точек Toll в штате Нью‑Йорк
/// на сайт калькулятора:
/// https://tollcalculator.thruway.ny.gov/index.aspx?Class=7&amp;Entry=m04x&amp;Exit=m37x
/// и вытаскивает из HTML только строку Total (NY E‑ZPass и NON‑NY E‑ZPass &amp; Tolls By Mail).
/// </summary>
public record ParseNewYorkTollPricesCommand(
    List<NewYorkTollRate> NewYorkTollRates,
    int VehicleClass = 5)
    : IRequest<ParseTollPricesResult>;

public class ParseNewYorkTollPricesCommandHandler(
    ITollDbContext _context)
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

        // 2.1. Устанавливаем StateCalculatorId только для Toll, найденных по Number
        var tollsToUpdate = tollsByNumber.Values
            .SelectMany(tolls => tolls)
            .Where(t => t.StateCalculatorId == null)
            .ToList();
        if (tollsToUpdate.Count > 0)
        {
            foreach (var toll in tollsToUpdate)
            {
                toll.StateCalculatorId = nyCalculator.Id;
            }
            await _context.SaveChangesAsync(ct);
        }

        // 3. Загружаем уже существующие цены для этого калькулятора
        var existingPrices = await _context.CalculatePrices
            .Where(cp => cp.StateCalculatorId == nyCalculator.Id)
            .Include(cp => cp.TollPrices)
            .ToListAsync(ct);

        // 3.1. Используем тарифы, переданные в команде
        var nyRates = request.NewYorkTollRates ?? [];

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

                var entryCode = entryNumber;
                var exitCode = exitNumber;

                if (entryCode == null || exitCode == null)
                {
                    notFoundPairs.Add($"{pairKey}: cannot map numbers to Entry/Exit codes");
                    continue;
                }

                // Ищем запись в JSON по коду въезда/выезда
                var rate = nyRates.FirstOrDefault(r =>
                    string.Equals(r.Entry, entryCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.Exit, exitCode, StringComparison.OrdinalIgnoreCase) &&
                    (r.Error == null || r.Error == string.Empty) &&
                    string.Equals(r.Status, "OK", StringComparison.OrdinalIgnoreCase));

                if (rate == null)
                {
                    notFoundPairs.Add($"{pairKey}: not found in JSON rates");
                    continue;
                }

                var ezPass = (double)rate.Ny;      // NY E‑ZPass
                var mailRate = (double)rate.NonNy; // NON‑NY E‑ZPass & Tolls By Mail

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

                        // Определяем тип осей по VehicleClass (по умолчанию _5L)
                        var axelType = Enum.IsDefined(typeof(AxelType), request.VehicleClass)
                            ? (AxelType)request.VehicleClass
                            : AxelType._6L;

                        if (existingPrice != null)
                        {
                            UpsertTollPrice(existingPrice, fromToll.Id, ezPass, TollPaymentType.EZPass, axelType);
                            UpsertTollPrice(existingPrice, fromToll.Id, mailRate, TollPaymentType.Cash, axelType);
                        }
                        else
                        {
                            var calculatePrice = new CalculatePrice
                            {
                                Id = Guid.NewGuid(),
                                StateCalculatorId = nyCalculator.Id,
                                FromId = fromToll.Id,
                                ToId = toToll.Id,
                                IPass = ezPass,
                                Cash = mailRate,
                                Online = mailRate
                            };
                            calculatePrice.TollPrices ??= [];

                            calculatePrice.TollPrices.Add(CreateTollPrice(
                                calculatePrice.Id,
                                fromToll.Id,
                                ezPass,
                                TollPaymentType.EZPass,
                                axelType));

                            calculatePrice.TollPrices.Add(CreateTollPrice(
                                calculatePrice.Id,
                                fromToll.Id,
                                mailRate,
                                TollPaymentType.Cash,
                                axelType));

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

    public class NewYorkTollRate
    {
        public string Entry { get; set; } = string.Empty;
        public string Exit { get; set; } = string.Empty;
        public decimal Ny { get; set; }
        public decimal NonNy { get; set; }
        public double Miles { get; set; }
        public string? Error { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}





