using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.DE;

public record ParseDelawareTollPricesCommand(string JsonPayload)
    : IRequest<ParseDelawareTollPricesResult>;

public record DelawareTollRoute(
    [property: System.Text.Json.Serialization.JsonPropertyName("direction")] string Direction,
    [property: System.Text.Json.Serialization.JsonPropertyName("entry")] int Entry,
    [property: System.Text.Json.Serialization.JsonPropertyName("exit")] int Exit,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass")] double EzPass,
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] double Cash);

public record DelawareTollResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("state")] string? State,
    [property: System.Text.Json.Serialization.JsonPropertyName("road")] string? Road,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_class")] string? Vehicle_Class,
    [property: System.Text.Json.Serialization.JsonPropertyName("time_period")] string? Time_Period,
    [property: System.Text.Json.Serialization.JsonPropertyName("routes")] List<DelawareTollRoute>? Routes);

public record ParseDelawareTollPricesResult(
    List<DelawareFoundTollInfo> FoundTolls,
    List<string> NotFoundPlazas,
    string? Error = null);

public record DelawareFoundTollInfo(
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber,
    string SearchKey);

public class ParseDelawareTollPricesCommandHandler(
    ITollDbContext _context,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<ParseDelawareTollPricesCommand, ParseDelawareTollPricesResult>
{
    // Delaware bounds: (south, west, north, east) = (38.4, -75.8, 39.7, -75.0)
    private static readonly double DeMinLatitude = 38.4;
    private static readonly double DeMinLongitude = -75.8;
    private static readonly double DeMaxLatitude = 39.7;
    private static readonly double DeMaxLongitude = -75.0;

    public async Task<ParseDelawareTollPricesResult> Handle(ParseDelawareTollPricesCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new ParseDelawareTollPricesResult(new(), new(), "JSON payload is empty");
        }

        DelawareTollResponse? data;
        try
        {
            await Task.Yield(); // keep async signature
            data = JsonSerializer.Deserialize<DelawareTollResponse>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new ParseDelawareTollPricesResult(new(), new(), $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.Routes == null || data.Routes.Count == 0)
        {
            return new ParseDelawareTollPricesResult(new(), new(), "Маршруты не найдены в ответе");
        }

        // Получаем или создаем StateCalculator для Delaware
        var delawareCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
            stateCode: "DE",
            calculatorName: "Delaware Turnpike",
            ct);

        // Создаем bounding box для Delaware
        var delawareBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            DeMinLongitude,
            DeMinLatitude,
            DeMaxLongitude,
            DeMaxLatitude);

        // Собираем все уникальные номера плаз из JSON (entry и exit)
        var allPlazaNumbers = new HashSet<string>();

        foreach (var route in data.Routes)
        {
            allPlazaNumbers.Add(route.Entry.ToString());
            allPlazaNumbers.Add(route.Exit.ToString());
        }

        // Один батч-запрос для поиска всех толлов по номерам
        var tollsByNumber = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allPlazaNumbers,
            delawareBoundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        // Фильтруем результаты: оставляем только точные совпадения по Key или Name
        var exactMatchesByNumber = new Dictionary<string, List<Toll>>();
        var allFoundTolls = new HashSet<Toll>(new TollIdComparer());

        foreach (var kvp in tollsByNumber)
        {
            var searchNumber = kvp.Key;
            var tollsForKey = kvp.Value;

            // Фильтруем: должны точно совпадать по Key или Name с номером
            var exactMatches = tollsForKey
                .Where(t =>
                    (t.Key != null && (t.Key == searchNumber || t.Key.Equals(searchNumber, StringComparison.OrdinalIgnoreCase))) ||
                    (t.Name != null && (t.Name == searchNumber || t.Name.Equals(searchNumber, StringComparison.OrdinalIgnoreCase))) ||
                    (t.Number != null && t.Number == searchNumber))
                .ToList();

            if (exactMatches.Count > 0)
            {
                exactMatchesByNumber[searchNumber] = exactMatches;
                foreach (var toll in exactMatches)
                {
                    allFoundTolls.Add(toll);
                }
            }
        }

        // Устанавливаем Number и StateCalculatorId для всех найденных толлов через сервис
        foreach (var kvp in exactMatchesByNumber)
        {
            var number = kvp.Key;
            var tolls = kvp.Value;

            _tollNumberService.SetNumberAndCalculatorId(
                tolls,
                number,
                delawareCalculator.Id,
                updateNumberIfDifferent: false);
        }

        // Создаем словарь для пар толлов с ценами: ключ - (FromId, ToId), значение - список TollPrice
        var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

        // Обрабатываем каждый маршрут для создания CalculatePrice и TollPrice
        foreach (var route in data.Routes)
        {
            var entryNumber = route.Entry.ToString();
            var exitNumber = route.Exit.ToString();

            // Получаем толлы для entry и exit
            if (!exactMatchesByNumber.TryGetValue(entryNumber, out var entryTolls) || entryTolls.Count == 0)
            {
                continue; // Пропускаем, если entry не найден
            }

            if (!exactMatchesByNumber.TryGetValue(exitNumber, out var exitTolls) || exitTolls.Count == 0)
            {
                continue; // Пропускаем, если exit не найден
            }

            var description = $"{route.Entry}-{route.Direction} в {route.Exit}-{route.Direction}";

            // Обрабатываем все комбинации entry -> exit толлов
            var pairResults = TollPairProcessor.ProcessAllPairsToDictionaryList(
                entryTolls,
                exitTolls,
                (entryToll, exitToll) =>
                {
                    var tollPrices = new List<TollPrice>();

                    // Создаем TollPrice для EZPass
                    if (route.EzPass > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.EZPass,
                            AxelType = AxelType._5L,
                            Amount = route.EzPass,
                            Description = description
                        });
                    }

                    // Создаем TollPrice для Cash
                    if (route.Cash > 0)
                    {
                        tollPrices.Add(new TollPrice
                        {
                            TollId = entryToll.Id,
                            PaymentType = TollPaymentType.Cash,
                            AxelType = AxelType._5L,
                            Amount = route.Cash,
                            Description = description
                        });
                    }

                    return tollPrices;
                });

            // Объединяем результаты с общим словарем
            foreach (var kvp in pairResults)
            {
                if (!tollPairsWithPrices.TryGetValue(kvp.Key, out var existingList))
                {
                    existingList = new List<TollPrice>();
                    tollPairsWithPrices[kvp.Key] = existingList;
                }
                existingList.AddRange(kvp.Value);
            }
        }

        // Батч-создание/обновление CalculatePrice с TollPrice
        if (tollPairsWithPrices.Count > 0)
        {
            var tollPairsWithPricesEnumerable = tollPairsWithPrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPrice>?)kvp.Value);

            await _calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                tollPairsWithPricesEnumerable,
                delawareCalculator.Id,
                includeTollPrices: true,
                ct);
        }

        // Формируем результат: найденные толлы
        var foundTolls = allFoundTolls.Select(toll =>
        {
            // Находим номер плазы, по которому был найден этот толл
            var searchNumber = exactMatchesByNumber
                .FirstOrDefault(kvp => kvp.Value.Contains(toll, new TollIdComparer()))
                .Key ?? toll.Number ?? "unknown";

            return new DelawareFoundTollInfo(
                toll.Id,
                toll.Name,
                toll.Key,
                toll.Number,
                searchNumber);
        }).ToList();

        // Формируем список ненайденных плаз
        var notFoundPlazas = new List<string>();
        foreach (var plazaNumber in allPlazaNumbers)
        {
            if (!exactMatchesByNumber.ContainsKey(plazaNumber))
            {
                notFoundPlazas.Add($"Plaza: {plazaNumber}");
            }
        }

        // Сохраняем все изменения (Toll, CalculatePrice, TollPrice)
        await _context.SaveChangesAsync(ct);

        return new ParseDelawareTollPricesResult(
            foundTolls,
            notFoundPlazas.Distinct().ToList());
    }

    /// <summary>
    /// Компаратор для сравнения толлов по Id
    /// </summary>
    private class TollIdComparer : IEqualityComparer<Toll>
    {
        public bool Equals(Toll? x, Toll? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Id == y.Id;
        }

        public int GetHashCode(Toll obj)
        {
            return obj.Id.GetHashCode();
        }
    }
}

