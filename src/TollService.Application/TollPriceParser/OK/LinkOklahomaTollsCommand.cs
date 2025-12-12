using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.OK;

public record OklahomaTollRate(
    [property: System.Text.Json.Serialization.JsonPropertyName("entryName")] string EntryName,
    [property: System.Text.Json.Serialization.JsonPropertyName("exitName")] string ExitName,
    [property: System.Text.Json.Serialization.JsonPropertyName("pikePassRate")] double PikePassRate,
    [property: System.Text.Json.Serialization.JsonPropertyName("cashCashlessRate")] double CashCashlessRate,
    [property: System.Text.Json.Serialization.JsonPropertyName("createdDate")] string? CreatedDate);

public record LinkOklahomaTollsCommand(List<int> TurnpikeIds) : IRequest<LinkOklahomaTollsResult>;

public record OklahomaFoundTollInfo(
    string EntryName,
    string ExitName,
    Guid? FromTollId,
    string? FromTollName,
    string? FromTollKey,
    Guid? ToTollId,
    string? ToTollName,
    string? ToTollKey,
    int TurnpikeId,
    string? TurnpikeName,
    int VehicleClass);

public record LinkOklahomaTollsResult(
    List<OklahomaFoundTollInfo> FoundTolls,
    List<string> NotFoundEntries,
    List<string> NotFoundExits,
    List<string> Errors,
    string? Error = null);

public class LinkOklahomaTollsCommandHandler(
    ITollDbContext _context,
    IHttpClientFactory _httpClientFactory,
    StateCalculatorService _stateCalculatorService,
    TollSearchService _tollSearchService,
    TollNumberService _tollNumberService,
    CalculatePriceService _calculatePriceService) : IRequestHandler<LinkOklahomaTollsCommand, LinkOklahomaTollsResult>
{
    // Oklahoma bounds: (south, west, north, east)
    private static readonly double OkMinLatitude = 33.6;
    private static readonly double OkMinLongitude = -103.0;
    private static readonly double OkMaxLatitude = 37.0;
    private static readonly double OkMaxLongitude = -94.4;

    private const string BaseApiUrl = "https://ppentapi.pikepass.com/api/sharedlookuppublic/turnpikes";

    public async Task<LinkOklahomaTollsResult> Handle(LinkOklahomaTollsCommand request, CancellationToken ct)
    {
        if (request.TurnpikeIds == null || request.TurnpikeIds.Count == 0)
        {
            return new LinkOklahomaTollsResult(
                new(),
                new(),
                new(),
                new(),
                "TurnpikeIds не может быть пустым");
        }

        // Получаем или создаем StateCalculator для Oklahoma
        var oklahomaCalculator = await _stateCalculatorService.GetOrCreateStateCalculatorAsync(
            stateCode: "OK",
            calculatorName: "Oklahoma Turnpike",
            ct);

        // Создаем bounding box для Oklahoma
        var okBoundingBox = BoundingBoxHelper.CreateBoundingBox(
            OkMinLongitude,
            OkMinLatitude,
            OkMaxLongitude,
            OkMaxLatitude);

        var foundTolls = new List<OklahomaFoundTollInfo>();
        var notFoundEntries = new List<string>();
        var notFoundExits = new List<string>();
        var errors = new List<string>();

        // Словарь для сбора пар с их TollPrice: ключ - (FromId, ToId), значение - список TollPrice
        var tollPairsWithPrices = new Dictionary<(Guid FromId, Guid ToId), List<TollPrice>>();

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        // Обрабатываем запросы для классов 5 и 6 (аксели)
        var vehicleClasses = new[] { 5, 6 };

        // Структура для хранения всех данных о тарифах перед обработкой
        var allTollRatesData = new List<(int TurnpikeId, int VehicleClass, List<OklahomaTollRate> TollRates)>();

        // Сначала собираем все данные из API
        foreach (var turnpikeId in request.TurnpikeIds)
        {
            foreach (var vehicleClass in vehicleClasses)
            {
                try
                {
                    var url = $"{BaseApiUrl}/{turnpikeId}/class/{vehicleClass}/tollrates";
                    var jsonResponse = await httpClient.GetStringAsync(url, ct);

                    var tollRates = JsonSerializer.Deserialize<List<OklahomaTollRate>>(jsonResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (tollRates == null || tollRates.Count == 0)
                    {
                        errors.Add($"Turnpike ID {turnpikeId}, Class {vehicleClass}: нет данных");
                        continue;
                    }

                    allTollRatesData.Add((turnpikeId, vehicleClass, tollRates));
                }
                catch (HttpRequestException httpEx)
                {
                    errors.Add($"Turnpike ID {turnpikeId}, Class {vehicleClass}: HTTP ошибка - {httpEx.Message}");
                }
                catch (JsonException jsonEx)
                {
                    errors.Add($"Turnpike ID {turnpikeId}, Class {vehicleClass}: Ошибка парсинга JSON - {jsonEx.Message}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Turnpike ID {turnpikeId}, Class {vehicleClass}: Ошибка - {ex.Message}");
                }
            }
        }

        // Собираем все уникальные имена entry и exit для оптимизированного поиска
        var allTollNames = new HashSet<string>();
        foreach (var (_, _, tollRates) in allTollRatesData)
        {
            foreach (var rate in tollRates)
            {
                if (!string.IsNullOrWhiteSpace(rate.EntryName))
                {
                    allTollNames.Add(rate.EntryName);
                }
                if (!string.IsNullOrWhiteSpace(rate.ExitName))
                {
                    allTollNames.Add(rate.ExitName);
                }
            }
        }

        // Один батч-запрос для поиска всех толлов
        var tollsByName = await _tollSearchService.FindMultipleTollsInBoundingBoxAsync(
            allTollNames,
            okBoundingBox,
            TollSearchOptions.NameOrKey,
            ct);

        // Устанавливаем Number и StateCalculatorId для всех найденных толлов
        foreach (var tollName in allTollNames)
        {
            if (tollsByName.TryGetValue(tollName, out var foundTollsForName) && foundTollsForName.Count > 0)
            {
                _tollNumberService.SetNumberAndCalculatorId(
                    foundTollsForName,
                    tollName,
                    oklahomaCalculator.Id,
                    updateNumberIfDifferent: true);
            }
        }

        // Теперь обрабатываем все собранные данные
        foreach (var (turnpikeId, vehicleClass, tollRates) in allTollRatesData)
        {
            // Получаем название дороги из кэша
            string? turnpikeName = GetTurnpikeName(turnpikeId);

            // Определяем AxelType из vehicleClass
            var axelType = vehicleClass switch
            {
                5 => AxelType._5L,
                6 => AxelType._6L,
                _ => throw new InvalidOperationException($"Неподдерживаемый класс транспортного средства: {vehicleClass}. Поддерживаются только классы 5 и 6.")
            };

            foreach (var rate in tollRates)
            {
                if (string.IsNullOrWhiteSpace(rate.EntryName) || string.IsNullOrWhiteSpace(rate.ExitName))
                {
                    continue;
                }

                // Получаем entry tolls из словаря
                if (!tollsByName.TryGetValue(rate.EntryName, out var entryTolls) || entryTolls.Count == 0)
                {
                    var entryKey = $"{rate.EntryName} (Turnpike {turnpikeId}, Class {vehicleClass})";
                    if (!notFoundEntries.Contains(entryKey))
                    {
                        notFoundEntries.Add(entryKey);
                    }
                    continue;
                }

                // Получаем exit tolls из словаря
                if (!tollsByName.TryGetValue(rate.ExitName, out var exitTolls) || exitTolls.Count == 0)
                {
                    var exitKey = $"{rate.ExitName} (Turnpike {turnpikeId}, Class {vehicleClass})";
                    if (!notFoundExits.Contains(exitKey))
                    {
                        notFoundExits.Add(exitKey);
                    }
                    continue;
                }

                var description = $"{rate.EntryName} -> {rate.ExitName} ({turnpikeName}, Class {vehicleClass})";

                // Обрабатываем все комбинации entry -> exit толлов
                var pairResults = TollPairProcessor.ProcessAllPairsToDictionaryList(
                    entryTolls,
                    exitTolls,
                    (entryToll, exitToll) =>
                    {
                        var tollPrices = new List<TollPrice>();

                        // Создаем TollPrice для EZPass (pikePassRate)
                        if (rate.PikePassRate > 0)
                        {
                            tollPrices.Add(new TollPrice
                            {
                                TollId = entryToll.Id,
                                PaymentType = TollPaymentType.EZPass,
                                AxelType = axelType,
                                Amount = rate.PikePassRate,
                                Description = description
                            });
                        }

                        // Создаем TollPrice для Cash (cashCashlessRate)
                        if (rate.CashCashlessRate > 0)
                        {
                            tollPrices.Add(new TollPrice
                            {
                                TollId = entryToll.Id,
                                PaymentType = TollPaymentType.Cash,
                                AxelType = axelType,
                                Amount = rate.CashCashlessRate,
                                Description = description
                            });
                        }

                        // Добавляем в foundTolls
                        foundTolls.Add(new OklahomaFoundTollInfo(
                            EntryName: rate.EntryName,
                            ExitName: rate.ExitName,
                            FromTollId: entryToll.Id,
                            FromTollName: entryToll.Name,
                            FromTollKey: entryToll.Key,
                            ToTollId: exitToll.Id,
                            ToTollName: exitToll.Name,
                            ToTollKey: exitToll.Key,
                            TurnpikeId: turnpikeId,
                            TurnpikeName: turnpikeName,
                            VehicleClass: vehicleClass));

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
        }

        // Батч-создание/обновление CalculatePrice с TollPrice
        if (tollPairsWithPrices.Count > 0)
        {
            var tollPairsWithPricesEnumerable = tollPairsWithPrices.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TollPrice>?)kvp.Value);

            await _calculatePriceService.GetOrCreateCalculatePricesBatchAsync(
                tollPairsWithPricesEnumerable,
                oklahomaCalculator.Id,
                includeTollPrices: true,
                ct);
        }

        // Сохраняем все изменения (Toll, CalculatePrice, TollPrice)
        await _context.SaveChangesAsync(ct);

        return new LinkOklahomaTollsResult(
            foundTolls,
            notFoundEntries.Distinct().ToList(),
            notFoundExits.Distinct().ToList(),
            errors);
    }


    private static readonly Dictionary<int, string> TurnpikeNames = new()
    {
        // {"turnpikeIds": [8]} json example 
        { 1, "TURNER TURNPIKE" },
        { 2, "WILL ROGERS TURNPIKE" },
        { 3, "H.E.BAILEY TURNPIKE" },
        { 4, "MUSKOGEE TURNPIKE" },
        { 5, "INDIAN NATION TURNPIKE" },
        { 6, "CIMARRON TURNPIKE" },
        { 7, "KILPATRICK TURNPIKE" },
        { 8, "CHEROKEE TURNPIKE" },
        { 9, "CHICKASAW TURNPIKE" },
        { 10, "CREEK TURNPIKE" },
        { 11, "KICKAPOO TURNPIKE" },
        { 12, "SOUTHWEST JKT TURNPIKE" },
        { 13, "GILCREASE TURNPIKE" }
    };

    private static string? GetTurnpikeName(int turnpikeId)
    {
        return TurnpikeNames.TryGetValue(turnpikeId, out var name) ? name : null;
    }
}

