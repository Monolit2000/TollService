using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.MA;

public record MassachusettsTollPriceDataItem(
    [property: JsonPropertyName("EntryNumber")] string? EntryNumber,
    [property: JsonPropertyName("ExitNumber")] string? ExitNumber,
    [property: JsonPropertyName("entry")] string? Entry,
    [property: JsonPropertyName("exit")] string? Exit,
    [property: JsonPropertyName("axles")] string? Axles,
    [property: JsonPropertyName("payment")] string? Payment,
    [property: JsonPropertyName("eastbound")] MassachusettsDirectionData? Eastbound,
    [property: JsonPropertyName("westbound")] MassachusettsDirectionData? Westbound,
    [property: JsonPropertyName("status")] string? Status);

public record MassachusettsDirectionData(
    [property: JsonPropertyName("toll")] string? Toll,
    [property: JsonPropertyName("mileage")] string? Mileage,
    [property: JsonPropertyName("time")] string? Time);

public record MassachusettsTollPricesRequest(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("road")] string? Road,
    [property: JsonPropertyName("axles")] string? Axles,
    [property: JsonPropertyName("payment")] string? Payment,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("ok")] int? Ok,
    [property: JsonPropertyName("data")] List<MassachusettsTollPriceDataItem>? Data);

public record MassachusettsLinkedTollInfo(
    string EntryNumber,
    string ExitNumber,
    Guid? EntryTollId,
    Guid? ExitTollId,
    string? EntryTollName,
    string? ExitTollName,
    List<MassachusettsTollPriceInfo> Prices);

public record MassachusettsTollPriceInfo(
    string PaymentType,
    double Amount,
    int Axles,
    string Direction);

public record ParseMassachusettsTollPricesCommand(
    string JsonPayload) : IRequest<ParseMassachusettsTollPricesResult>;

public record ParseMassachusettsTollPricesResult(
    List<MassachusettsLinkedTollInfo> LinkedTolls,
    List<string> NotFoundEntries,
    List<string> NotFoundExits,
    int UpdatedTollsCount,
    string? Error = null);

// Massachusetts bounds: (south, west, north, east) = (41.2, -73.5, 42.9, -69.9)
public class ParseMassachusettsTollPricesCommandHandler(
    ITollDbContext _context,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseMassachusettsTollPricesCommand, ParseMassachusettsTollPricesResult>
{
    private static readonly double MaMinLatitude = 41.2;
    private static readonly double MaMinLongitude = -73.5;
    private static readonly double MaMaxLatitude = 42.9;
    private static readonly double MaMaxLongitude = -69.9;

    public async Task<ParseMassachusettsTollPricesResult> Handle(ParseMassachusettsTollPricesCommand request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JsonPayload))
            {
                return new ParseMassachusettsTollPricesResult(
                    new(),
                    new(),
                    new(),
                    0,
                    "JSON payload is empty");
            }

            MassachusettsTollPricesRequest? data;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                data = JsonSerializer.Deserialize<MassachusettsTollPricesRequest>(request.JsonPayload, options);
            }
            catch (JsonException jsonEx)
            {
                return new ParseMassachusettsTollPricesResult(
                    new(),
                    new(),
                    new(),
                    0,
                    $"Ошибка парсинга JSON: {jsonEx.Message}");
            }

            if (data?.Data == null || data.Data.Count == 0)
            {
                return new ParseMassachusettsTollPricesResult(
                    new(),
                    new(),
                    new(),
                    0,
                    "Данные не найдены в JSON");
            }

            // Создаем bounding box для Massachusetts
            var maBoundingBox = BoundingBoxHelper.CreateBoundingBox(
                MaMinLatitude, MaMinLongitude, MaMaxLatitude, MaMaxLongitude);

            // Получаем или создаем StateCalculator для Massachusetts
            var stateCalculator = await _context.StateCalculators
                .FirstOrDefaultAsync(sc => sc.StateCode == "MA", ct);

            if (stateCalculator == null)
            {
                stateCalculator = new StateCalculator
                {
                    Id = Guid.NewGuid(),
                    Name = "Massachusetts Turnpike",
                    StateCode = "MA"
                };
                _context.StateCalculators.Add(stateCalculator);
                await _context.SaveChangesAsync(ct);
            }

            // Собираем все уникальные EntryNumber и ExitNumber
            var allEntryNumbers = data.Data
                .Where(d => !string.IsNullOrWhiteSpace(d.EntryNumber))
                .Select(d => d.EntryNumber!)
                .Distinct()
                .ToList();

            var allExitNumbers = data.Data
                .Where(d => !string.IsNullOrWhiteSpace(d.ExitNumber))
                .Select(d => d.ExitNumber!)
                .Distinct()
                .ToList();

            // Загружаем все tolls в пределах bounding box один раз
            var allTollsInBox = await _context.Tolls
                .Where(t => t.Location != null && maBoundingBox.Contains(t.Location))
                .ToListAsync(ct);

            // Создаем словари для быстрого поиска по Number
            var tollsByEntryNumber = allTollsInBox
                .Where(t => !string.IsNullOrWhiteSpace(t.Number) && allEntryNumbers.Contains(t.Number))
                .GroupBy(t => t.Number!)
                .ToDictionary(g => g.Key, g => g.ToList());

            var tollsByExitNumber = allTollsInBox
                .Where(t => !string.IsNullOrWhiteSpace(t.Number) && allExitNumbers.Contains(t.Number))
                .GroupBy(t => t.Number!)
                .ToDictionary(g => g.Key, g => g.ToList());

            var linkedTolls = new List<MassachusettsLinkedTollInfo>();
            var notFoundEntries = new List<string>();
            var notFoundExits = new List<string>();
            var calculatePricesToUpdate = new Dictionary<(Guid FromId, Guid ToId), List<(double Amount, TollPaymentType PaymentType, AxelType Axles, string Description)>>();

            foreach (var item in data.Data)
            {
                if (string.IsNullOrWhiteSpace(item.EntryNumber) || string.IsNullOrWhiteSpace(item.ExitNumber))
                    continue;

                // Ищем entry toll по Number
                if (!tollsByEntryNumber.TryGetValue(item.EntryNumber, out var entryTolls) || entryTolls.Count == 0)
                {
                    notFoundEntries.Add(item.EntryNumber);
                    continue;
                }

                // Ищем exit toll по Number
                if (!tollsByExitNumber.TryGetValue(item.ExitNumber, out var exitTolls) || exitTolls.Count == 0)
                {
                    notFoundExits.Add(item.ExitNumber);
                    continue;
                }

                // Берем первый найденный toll для entry и exit
                var entryToll = entryTolls.First();
                var exitToll = exitTolls.First();

                // Парсим axles
                var axles = ParseAxles(item.Axles ?? data.Axles);
                if (axles == AxelType.Unknown)
                {
                    continue; // Пропускаем если не удалось определить количество осей
                }

                // Парсим payment type
                var paymentType = ParsePaymentType(item.Payment ?? data.Payment);
                if (paymentType == null)
                {
                    continue; // Пропускаем если не удалось определить тип оплаты
                }

                var prices = new List<MassachusettsTollPriceInfo>();

                // Обрабатываем eastbound
                if (item.Eastbound?.Toll != null)
                {
                    var amount = ParseTollAmount(item.Eastbound.Toll);
                    if (amount.HasValue && amount.Value > 0)
                    {
                        prices.Add(new MassachusettsTollPriceInfo("E-Z Pass", amount.Value, (int)axles, "Eastbound"));

                        var key = (entryToll.Id, exitToll.Id);
                        if (!calculatePricesToUpdate.ContainsKey(key))
                        {
                            calculatePricesToUpdate[key] = new List<(double, TollPaymentType, AxelType, string)>();
                        }

                        calculatePricesToUpdate[key].Add((
                            amount.Value,
                            paymentType.Value,
                            axles,
                            $"Massachusetts {item.Entry} -> {item.Exit} - E-Z Pass (Eastbound, {axles} axles)"));
                    }
                }

                // Обрабатываем westbound
                if (item.Westbound?.Toll != null)
                {
                    var amount = ParseTollAmount(item.Westbound.Toll);
                    if (amount.HasValue && amount.Value > 0)
                    {
                        prices.Add(new MassachusettsTollPriceInfo("E-Z Pass", amount.Value, (int)axles, "Westbound"));

                        // Обратное направление для westbound
                        var key = (exitToll.Id, entryToll.Id);
                        if (!calculatePricesToUpdate.ContainsKey(key))
                        {
                            calculatePricesToUpdate[key] = new List<(double, TollPaymentType, AxelType, string)>();
                        }

                        calculatePricesToUpdate[key].Add((
                            amount.Value,
                            paymentType.Value,
                            axles,
                            $"Massachusetts {item.Exit} -> {item.Entry} - E-Z Pass (Westbound, {axles} axles)"));
                    }
                }

                // Добавляем информацию о связанном toll с ценами
                if (prices.Count > 0)
                {
                    linkedTolls.Add(new MassachusettsLinkedTollInfo(
                        EntryNumber: item.EntryNumber,
                        ExitNumber: item.ExitNumber,
                        EntryTollId: entryToll.Id,
                        ExitTollId: exitToll.Id,
                        EntryTollName: entryToll.Name,
                        ExitTollName: exitToll.Name,
                        Prices: prices));
                }
            }

            // Батч-установка цен через CalculatePrice
            int updatedTollsCount = 0;
            foreach (var kvp in calculatePricesToUpdate)
            {
                var (fromId, toId) = kvp.Key;
                var pricesData = kvp.Value;

                // Создаем или получаем CalculatePrice
                var calculatePrice = await _calculatePriceService.GetOrCreateCalculatePriceAsync(
                    fromId,
                    toId,
                    stateCalculator.Id,
                    includeTollPrices: true,
                    ct);

                // Устанавливаем цены
                foreach (var (amount, paymentType, axles, description) in pricesData)
                {
                    var tollPrice = _calculatePriceService.SetTollPrice(
                        calculatePrice,
                        fromId, // Используем fromId как TollId
                        amount,
                        paymentType,
                        axles,
                        description: description);

                    if (tollPrice.Id == Guid.Empty)
                    {
                        updatedTollsCount++;
                    }
                }
            }

            // Сохраняем все изменения
            await _context.SaveChangesAsync(ct);

            return new ParseMassachusettsTollPricesResult(
                linkedTolls,
                notFoundEntries.Distinct().ToList(),
                notFoundExits.Distinct().ToList(),
                updatedTollsCount);
        }
        catch (Exception ex)
        {
            return new ParseMassachusettsTollPricesResult(
                new(),
                new(),
                new(),
                0,
                $"Ошибка при обработке: {ex.Message}");
        }
    }

    private static AxelType ParseAxles(string? axles)
    {
        if (string.IsNullOrWhiteSpace(axles))
            return AxelType.Unknown;

        var axlesLower = axles.ToLower();
        if (axlesLower.Contains("5") || axlesLower.Contains("5 axle"))
            return AxelType._5L;
        if (axlesLower.Contains("7") || axlesLower.Contains("7 axle"))
            return AxelType._6L;
        if (axlesLower.Contains("4") || axlesLower.Contains("4 axle"))
            return AxelType._4L;
        if (axlesLower.Contains("3") || axlesLower.Contains("3 axle"))
            return AxelType._3L;
        if (axlesLower.Contains("2") || axlesLower.Contains("2 axle"))
            return AxelType._2L;
        if (axlesLower.Contains("1") || axlesLower.Contains("1 axle"))
            return AxelType._1L;

        return AxelType.Unknown;
    }

    private static TollPaymentType? ParsePaymentType(string? payment)
    {
        if (string.IsNullOrWhiteSpace(payment))
            return null;

        var paymentLower = payment.ToLower();
        if (paymentLower.Contains("e-z pass") || paymentLower.Contains("ezpass") || paymentLower.Contains("ez-pass"))
            return TollPaymentType.EZPass;
        if (paymentLower.Contains("out of state"))
            return TollPaymentType.OutOfStateEZPass;
        if (paymentLower.Contains("cash"))
            return TollPaymentType.Cash;
        if (paymentLower.Contains("pay online") || paymentLower.Contains("pay-by-plate") || paymentLower.Contains("pay by plate"))
            return TollPaymentType.PayOnline;

        return TollPaymentType.EZPass; // По умолчанию E-Z Pass для Massachusetts
    }

    private static double? ParseTollAmount(string? toll)
    {
        if (string.IsNullOrWhiteSpace(toll))
            return null;

        // Удаляем символ доллара и пробелы
        var cleaned = toll.Replace("$", "").Replace(",", "").Trim();

        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        return null;
    }
}

