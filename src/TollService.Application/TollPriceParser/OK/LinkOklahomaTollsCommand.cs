using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
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
    IHttpClientFactory _httpClientFactory) : IRequestHandler<LinkOklahomaTollsCommand, LinkOklahomaTollsResult>
{
    // Oklahoma bounds: (south, west, north, east)
    private static readonly double OkMinLatitude = 33.6;
    private static readonly double OkMinLongitude = -103.0;
    private static readonly double OkMaxLatitude = 37.0;
    private static readonly double OkMaxLongitude = -94.4;

    private const string BaseApiUrl = "https://ppentapi.pikepass.com/api/sharedlookuppublic/turnpikes";
    // {"turnpikeIds": [8]} json example 
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
        var oklahomaCalculator = await _context.StateCalculators
            .FirstOrDefaultAsync(sc => sc.StateCode == "OK", ct);

        if (oklahomaCalculator == null)
        {
            oklahomaCalculator = new StateCalculator
            {
                Id = Guid.NewGuid(),
                Name = "Oklahoma Turnpike",
                StateCode = "OK"
            };
            _context.StateCalculators.Add(oklahomaCalculator);
            await _context.SaveChangesAsync(ct);
        }

        // Создаем bounding box для Oklahoma
        var okBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(OkMinLongitude, OkMinLatitude),
            new Coordinate(OkMaxLongitude, OkMinLatitude),
            new Coordinate(OkMaxLongitude, OkMaxLatitude),
            new Coordinate(OkMinLongitude, OkMaxLatitude),
            new Coordinate(OkMinLongitude, OkMinLatitude)
        }))
        { SRID = 4326 };

        var foundTolls = new List<OklahomaFoundTollInfo>();
        var notFoundEntries = new List<string>();
        var notFoundExits = new List<string>();
        var errors = new List<string>();

        // Списки для батч-вставки
        var calculatePricesToAdd = new List<CalculatePrice>();
        var tollPricesToAdd = new List<TollPrice>();
        var tollPriceIdsSet = new HashSet<Guid>(); // Для отслеживания уже добавленных TollPrice по Id

        // Кэш для CalculatePrice: ключ = (FromId, ToId)
        var calculatePriceCache = new Dictionary<(Guid FromId, Guid ToId), CalculatePrice>();

        // Собираем все пары (FromId, ToId) для батч-загрузки CalculatePrice
        var tollPairsToProcess = new HashSet<(Guid FromId, Guid ToId)>();
        var tollRatesData = new List<(OklahomaTollRate Rate, List<Toll> EntryTolls, List<Toll> ExitTolls, int TurnpikeId, string? TurnpikeName, int VehicleClass)>();

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        // Обрабатываем запросы для классов 5 и 6 (аксели)
        var vehicleClasses = new[] { 5, 6 };

        // Первый проход: собираем все данные и пары tolls
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

                    // Получаем название дороги из кэша
                    string? turnpikeName = GetTurnpikeName(turnpikeId);

                    foreach (var rate in tollRates)
                    {
                        if (string.IsNullOrWhiteSpace(rate.EntryName) || string.IsNullOrWhiteSpace(rate.ExitName))
                        {
                            continue;
                        }

                        // Ищем entry toll
                        var entryNameLower = rate.EntryName.ToLower();
                        var entryTolls = await FindTollsInOklahoma(entryNameLower, okBoundingBox, ct);

                        if (entryTolls.Count == 0)
                        {
                            var entryKey = $"{rate.EntryName} (Turnpike {turnpikeId}, Class {vehicleClass})";
                            if (!notFoundEntries.Contains(entryKey))
                            {
                                notFoundEntries.Add(entryKey);
                            }
                        }

                        // Ищем exit toll
                        var exitNameLower = rate.ExitName.ToLower();
                        var exitTolls = await FindTollsInOklahoma(exitNameLower, okBoundingBox, ct);

                        if (exitTolls.Count == 0)
                        {
                            var exitKey = $"{rate.ExitName} (Turnpike {turnpikeId}, Class {vehicleClass})";
                            if (!notFoundExits.Contains(exitKey))
                            {
                                notFoundExits.Add(exitKey);
                            }
                        }

                        // Если нашли оба toll, сохраняем данные для обработки
                        if (entryTolls.Count > 0 && exitTolls.Count > 0)
                        {
                            // Собираем пары для батч-загрузки
                            foreach (var entryToll in entryTolls)
                            {
                                foreach (var exitToll in exitTolls)
                                {
                                    if (entryToll.Id != exitToll.Id)
                                    {
                                        tollPairsToProcess.Add((entryToll.Id, exitToll.Id));
                                    }
                                }
                            }

                            // Сохраняем данные для второго прохода
                            tollRatesData.Add((rate, entryTolls, exitTolls, turnpikeId, turnpikeName, vehicleClass));
                        }
                    }
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

        // Батч-загрузка всех существующих CalculatePrice одним запросом
        if (tollPairsToProcess.Count > 0)
        {
            // Загружаем все существующие CalculatePrice, которые могут соответствовать нашим парам
            var fromIds = tollPairsToProcess.Select(p => p.FromId).Distinct().ToList();
            var toIds = tollPairsToProcess.Select(p => p.ToId).Distinct().ToList();

            var existingCalculatePrices = await _context.CalculatePrices
                .Include(cp => cp.TollPrices)
                .Where(cp =>
                    cp.StateCalculatorId == oklahomaCalculator.Id &&
                    fromIds.Contains(cp.FromId) &&
                    toIds.Contains(cp.ToId))
                .ToListAsync(ct);

            // Заполняем кэш существующими CalculatePrice (проверяем точное соответствие пары)
            foreach (var cp in existingCalculatePrices)
            {
                var key = (cp.FromId, cp.ToId);
                if (tollPairsToProcess.Contains(key))
                {
                    calculatePriceCache[key] = cp;
                }
            }

            // Создаем новые CalculatePrice для пар, которых нет в БД
            foreach (var pair in tollPairsToProcess)
            {
                if (!calculatePriceCache.ContainsKey(pair))
                {
                    var newCalculatePrice = new CalculatePrice
                    {
                        Id = Guid.NewGuid(),
                        StateCalculatorId = oklahomaCalculator.Id,
                        FromId = pair.FromId,
                        ToId = pair.ToId,
                        TollPrices = new List<TollPrice>()
                    };
                    calculatePriceCache[pair] = newCalculatePrice;
                    calculatePricesToAdd.Add(newCalculatePrice);
                }
            }
        }

        // Второй проход: обработка данных и создание/обновление цен
        foreach (var (rate, entryTolls, exitTolls, turnpikeId, turnpikeName, vehicleClass) in tollRatesData)
        {
            // Устанавливаем Number и StateCalculatorId для найденных tolls
            foreach (var entryToll in entryTolls)
            {
                if (entryToll.Number != rate.EntryName)
                {
                    entryToll.Number = rate.EntryName;
                }
                entryToll.StateCalculatorId = oklahomaCalculator.Id;
            }

            foreach (var exitToll in exitTolls)
            {
                if (exitToll.Number != rate.ExitName)
                {
                    exitToll.Number = rate.ExitName;
                }
                exitToll.StateCalculatorId = oklahomaCalculator.Id;
            }

            // Определяем AxelType из vehicleClass
            // Класс 5 = 5-осные аксели, Класс 6 = 6-осные аксели
            var axelType = vehicleClass switch
            {
                5 => AxelType._5L,
                6 => AxelType._6L,
                _ => throw new InvalidOperationException($"Неподдерживаемый класс транспортного средства: {vehicleClass}. Поддерживаются только классы 5 и 6.")
            };

            // Может быть несколько tolls с одинаковым именем, поэтому создаем записи для всех комбинаций
            foreach (var entryToll in entryTolls)
            {
                foreach (var exitToll in exitTolls)
                {
                    if (entryToll.Id == exitToll.Id)
                    {
                        continue; // Пропускаем, если entry и exit это один и тот же toll
                    }

                    // Получаем CalculatePrice из кэша
                    var cacheKey = (entryToll.Id, exitToll.Id);
                    if (!calculatePriceCache.TryGetValue(cacheKey, out var calculatePrice))
                    {
                        continue; // Не должно случиться, но на всякий случай
                    }

                    // Используем SetPriceByPaymentType для EZPass (pikePassRate)
                    if (rate.PikePassRate > 0)
                    {
                        var tollPrice = calculatePrice.SetPriceByPaymentType(
                            rate.PikePassRate,
                            TollPaymentType.EZPass,
                            axelType);

                        // Если это новый TollPrice (TollId == Guid.Empty означает, что он только что создан),
                        // нужно его настроить и добавить в список для сохранения
                        if (tollPrice.TollId == Guid.Empty)
                        {
                            tollPrice.TollId = entryToll.Id;
                            tollPrice.Description = $"{rate.EntryName} -> {rate.ExitName} ({turnpikeName}, Class {vehicleClass})";
                            
                            // Добавляем только если еще не добавлен (проверка по Id для избежания дублей)
                            if (tollPriceIdsSet.Add(tollPrice.Id))
                            {
                                tollPricesToAdd.Add(tollPrice);
                            }
                        }
                    }

                    // Используем SetPriceByPaymentType для Cash (cashCashlessRate)
                    if (rate.CashCashlessRate > 0)
                    {
                        var tollPrice = calculatePrice.SetPriceByPaymentType(
                            rate.CashCashlessRate,
                            TollPaymentType.Cash,
                            axelType);

                        // Если это новый TollPrice (TollId == Guid.Empty означает, что он только что создан),
                        // нужно его настроить и добавить в список для сохранения
                        if (tollPrice.TollId == Guid.Empty)
                        {
                            tollPrice.TollId = entryToll.Id;
                            tollPrice.Description = $"{rate.EntryName} -> {rate.ExitName} ({turnpikeName}, Class {vehicleClass})";
                            
                            // Добавляем только если еще не добавлен (проверка по Id для избежания дублей)
                            if (tollPriceIdsSet.Add(tollPrice.Id))
                            {
                                tollPricesToAdd.Add(tollPrice);
                            }
                        }
                    }

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
                }
            }
        }

        // Батч-вставка: сначала сохраняем изменения в Toll (Number, StateCalculatorId)
        // Это важно, чтобы все изменения были сохранены перед вставкой TollPrice
        await _context.SaveChangesAsync(ct);

        // Затем сохраняем новые CalculatePrice
        if (calculatePricesToAdd.Count > 0)
        {
            _context.CalculatePrices.AddRange(calculatePricesToAdd);
            await _context.SaveChangesAsync(ct);
        }

        // Затем добавляем новые TollPrice батчами
        if (tollPricesToAdd.Count > 0)
        {
            // Удаляем дубликаты по Id (на случай если один и тот же TollPrice был добавлен несколько раз)
            var uniqueTollPrices = tollPricesToAdd
                .GroupBy(tp => tp.Id)
                .Select(g => g.First())
                .Where(tp => tp.Id != Guid.Empty) // Убеждаемся, что Id установлен
                .ToList();

            // Получаем все существующие Id из БД для проверки
            var existingTollPriceIds = await _context.TollPrices
                .Where(tp => uniqueTollPrices.Select(utp => utp.Id).Contains(tp.Id))
                .Select(tp => tp.Id)
                .ToListAsync(ct);

            // Фильтруем только те TollPrice, которые:
            // 1. Не отслеживаются EF (не в Local)
            // 2. Не существуют в БД (не в existingTollPriceIds)
            var newTollPricesOnly = uniqueTollPrices
                .Where(tp => 
                    !_context.TollPrices.Local.Any(e => e.Id == tp.Id) &&
                    !existingTollPriceIds.Contains(tp.Id))
                .ToList();

            if (newTollPricesOnly.Count > 0)
            {
                const int batchSize = 500;
                for (int i = 0; i < newTollPricesOnly.Count; i += batchSize)
                {
                    var batch = newTollPricesOnly.Skip(i).Take(batchSize).ToList();
                    _context.TollPrices.AddRange(batch);
                    await _context.SaveChangesAsync(ct);
                }
            }
        }

        // Финальное сохранение всех изменений (EF отследит изменения автоматически для обновленных сущностей)
        await _context.SaveChangesAsync(ct);

        return new LinkOklahomaTollsResult(
            foundTolls,
            notFoundEntries.Distinct().ToList(),
            notFoundExits.Distinct().ToList(),
            errors);
    }

    private async Task<List<Toll>> FindTollsInOklahoma(string searchName, Polygon boundingBox, CancellationToken ct)
    {
        // Сначала ищем точное совпадение (без учета регистра)
        var tolls = await _context.Tolls
            .Where(t =>
                t.Location != null &&
                boundingBox.Contains(t.Location) &&
                ((t.Name != null && t.Name.ToLower() == searchName) ||
                 (t.Key != null && t.Key.ToLower() == searchName)))
            .ToListAsync(ct);

        // Если не нашли точное совпадение, пробуем частичное совпадение
        if (tolls.Count == 0)
        {
            var allTollsInBox = await _context.Tolls
                .Where(t =>
                    t.Location != null &&
                    boundingBox.Contains(t.Location))
                .ToListAsync(ct);

            tolls = allTollsInBox
                .Where(t =>
                    (t.Name != null && (t.Name.ToLower().Contains(searchName) || searchName.Contains(t.Name.ToLower()))) ||
                    (t.Key != null && (t.Key.ToLower().Contains(searchName) || searchName.Contains(t.Key.ToLower()))))
                .ToList();
        }

        // Фильтруем: исключаем tolls с пустыми или невалидными именами/ключами
        tolls = tolls
            .Where(t => IsValidTollNameOrKey(t.Name) || IsValidTollNameOrKey(t.Key))
            .ToList();

        return tolls;
    }

    private static bool IsValidTollNameOrKey(string? nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey))
            return false;

        var trimmed = nameOrKey.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "_" || trimmed.All(c => c == '_'))
            return false;

        return true;
    }

    private static readonly Dictionary<int, string> TurnpikeNames = new()
    {
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

