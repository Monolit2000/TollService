using System;
using System.Linq;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NH;

public record NewHampshireTollPlaza(
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("highway")] string? Highway,
    [property: System.Text.Json.Serialization.JsonPropertyName("tolls")] Dictionary<string, Dictionary<string, double>>? Tolls);

public record NewHampshirePricesData(
    [property: System.Text.Json.Serialization.JsonPropertyName("new_hampshire_turnpike_system_tolls")] NewHampshireTurnpikeSystem? NewHampshireTurnpikeSystemTolls);

public record NewHampshireTurnpikeSystem(
    [property: System.Text.Json.Serialization.JsonPropertyName("currency")] string? Currency,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_classes")] Dictionary<string, string>? VehicleClasses,
    [property: System.Text.Json.Serialization.JsonPropertyName("toll_plazas")] List<NewHampshireTollPlaza>? TollPlazas,
    [property: System.Text.Json.Serialization.JsonPropertyName("summary")] Dictionary<string, object>? Summary);

public record LinkNewHampshireTollsCommand(string JsonPayload)
    : IRequest<LinkNewHampshireTollsResult>;

public record NewHampshireFoundTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber);

public record LinkNewHampshireTollsResult(
    List<NewHampshireFoundTollInfo> FoundTolls,
    List<string> NotFoundPlazas,
    int UpdatedCount,
    int CreatedCount,
    string? Error = null);

public class LinkNewHampshireTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<LinkNewHampshireTollsCommand, LinkNewHampshireTollsResult>
{
    // New Hampshire bounds: approximate (south, west, north, east) = (42.7, -72.6, 45.3, -70.6)
    private static readonly double NhMinLatitude = 42.7;
    private static readonly double NhMinLongitude = -72.6;
    private static readonly double NhMaxLatitude = 45.3;
    private static readonly double NhMaxLongitude = -70.6;

    public async Task<LinkNewHampshireTollsResult> Handle(LinkNewHampshireTollsCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkNewHampshireTollsResult(new(), new(), 0, 0, "JSON payload is empty");
        }

        NewHampshirePricesData? data = null;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            await Task.Yield();
            // Пробуем десериализовать как обернутый объект
            data = JsonSerializer.Deserialize<NewHampshirePricesData>(request.JsonPayload, options);
        }
        catch (JsonException)
        {
            // Игнорируем, попробуем другой формат
        }

        // Если не удалось распарсить как обернутый объект, пробуем как прямой объект NewHampshireTurnpikeSystem
        if (data?.NewHampshireTurnpikeSystemTolls == null)
        {
            try
            {
                var directData = JsonSerializer.Deserialize<NewHampshireTurnpikeSystem>(request.JsonPayload, options);
                if (directData != null)
                {
                    data = new NewHampshirePricesData(directData);
                }
            }
            catch (JsonException jsonEx)
            {
                return new LinkNewHampshireTollsResult(new(), new(), 0, 0, $"Ошибка парсинга JSON: {jsonEx.Message}. Убедитесь, что JSON содержит поле 'new_hampshire_turnpike_system_tolls' с массивом 'toll_plazas'.");
            }
        }

        if (data?.NewHampshireTurnpikeSystemTolls?.TollPlazas == null || data.NewHampshireTurnpikeSystemTolls.TollPlazas.Count == 0)
        {
            // Проверяем, что вообще было распарсено
            if (data == null)
            {
                return new LinkNewHampshireTollsResult(new(), new(), 0, 0, "Не удалось распарсить JSON. Проверьте структуру данных.");
            }
            if (data.NewHampshireTurnpikeSystemTolls == null)
            {
                return new LinkNewHampshireTollsResult(new(), new(), 0, 0, "Поле 'new_hampshire_turnpike_system_tolls' не найдено в JSON.");
            }
            return new LinkNewHampshireTollsResult(new(), new(), 0, 0, "Плазы не найдены в ответе (массив 'toll_plazas' пуст или отсутствует).");
        }

        // Создаем bounding box для New Hampshire
        var nhBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(NhMinLongitude, NhMinLatitude),
            new Coordinate(NhMaxLongitude, NhMinLatitude),
            new Coordinate(NhMaxLongitude, NhMaxLatitude),
            new Coordinate(NhMinLongitude, NhMaxLatitude),
            new Coordinate(NhMinLongitude, NhMinLatitude)
        }))
        { SRID = 4326 };

        var foundTolls = new List<NewHampshireFoundTollInfo>();
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;
        int createdCount = 0;

        // Функция для маппинга класса на AxelType
        static AxelType MapClassToAxelType(string className)
        {
            return className.ToLower() switch
            {
                "class_5" => AxelType._5L,
                "class_6" => AxelType._6L,
                _ => AxelType.Unknown
            };
        }

        foreach (var plaza in data.NewHampshireTurnpikeSystemTolls.TollPlazas)
        {
            if (string.IsNullOrWhiteSpace(plaza.Name))
            {
                notFoundPlazas.Add("Plaza with empty name");
                continue;
            }

            // Ищем ВСЕ tolls по полю key в пределах New Hampshire
            // Сначала ищем точное совпадение (без учета регистра)
            var plazaNameLower = plaza.Name.ToLower();
            var tolls = await _context.Tolls
                .Where(t =>
                    t.Location != null &&
                    nhBoundingBox.Contains(t.Location) &&
                    t.Key != null &&
                    t.Key.ToLower() == plazaNameLower)
                .ToListAsync(ct);

            // Если не нашли точное совпадение, пробуем частичное совпадение
            if (tolls.Count == 0)
            {
                // Загружаем все tolls в пределах bounding box и фильтруем в памяти
                var allTollsInBox = await _context.Tolls
                    .Where(t =>
                        t.Location != null &&
                        nhBoundingBox.Contains(t.Location) &&
                        t.Key != null)
                    .ToListAsync(ct);

                tolls = allTollsInBox
                    .Where(t => 
                        t.Key!.ToLower().Contains(plazaNameLower) ||
                        plazaNameLower.Contains(t.Key.ToLower()))
                    .ToList();
            }

            if (tolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.Name);
                continue;
            }

            // Обрабатываем цены для всех найденных tolls
            foreach (var toll in tolls)
            {
                // Обрабатываем цены из plaza.Tolls
                if (plaza.Tolls != null && plaza.Tolls.Count > 0)
                {
                    foreach (var vehicleClass in plaza.Tolls)
                    {
                        var axelType = MapClassToAxelType(vehicleClass.Key);
                        if (axelType == AxelType.Unknown)
                        {
                            continue; // Пропускаем неизвестные классы
                        }

                        var classPrices = vehicleClass.Value;
                        if (classPrices == null || classPrices.Count == 0)
                        {
                            continue;
                        }

                        // Обрабатываем Cash цены (case-insensitive поиск)
                        var cashPrice = classPrices.FirstOrDefault(kvp => 
                            kvp.Key.Equals("cash", StringComparison.OrdinalIgnoreCase)).Value;
                        if (cashPrice > 0)
                        {
                            var existingCashPrice = await _context.TollPrices
                                .FirstOrDefaultAsync(tp =>
                                    tp.TollId == toll.Id &&
                                    tp.PaymentType == TollPaymentType.Cash &&
                                    tp.AxelType == axelType,
                                    ct);

                            if (existingCashPrice != null)
                            {
                                existingCashPrice.Amount = cashPrice;
                                updatedCount++;
                            }
                            else
                            {
                                var newCashPrice = new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = toll.Id,
                                    PaymentType = TollPaymentType.Cash,
                                    AxelType = axelType,
                                    Amount = cashPrice,
                                    Description = $"{plaza.Name} - {vehicleClass.Key}"
                                };
                                toll.AddTollPrice(newCashPrice);
                                _context.TollPrices.Add(newCashPrice);
                                createdCount++;
                            }
                        }

                        // Обрабатываем EZPass цены (case-insensitive поиск)
                        var ezpassPrice = classPrices.FirstOrDefault(kvp => 
                            kvp.Key.Equals("ezpass", StringComparison.OrdinalIgnoreCase)).Value;
                        if (ezpassPrice > 0)
                        {
                            var existingEzPassPrice = await _context.TollPrices
                                .FirstOrDefaultAsync(tp =>
                                    tp.TollId == toll.Id &&
                                    tp.PaymentType == TollPaymentType.EZPass &&
                                    tp.AxelType == axelType,
                                    ct);

                            if (existingEzPassPrice != null)
                            {
                                existingEzPassPrice.Amount = ezpassPrice;
                                updatedCount++;
                            }
                            else
                            {
                                var newEzPassPrice = new TollPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TollId = toll.Id,
                                    PaymentType = TollPaymentType.EZPass,
                                    AxelType = axelType,
                                    Amount = ezpassPrice,
                                    Description = $"{plaza.Name} - {vehicleClass.Key}"
                                };
                                toll.AddTollPrice(newEzPassPrice);
                                _context.TollPrices.Add(newEzPassPrice);
                                createdCount++;
                            }
                        }
                    }
                }

                foundTolls.Add(new NewHampshireFoundTollInfo(
                    PlazaName: plaza.Name,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    TollKey: toll.Key,
                    TollNumber: toll.Number));
            }
        }

        await _context.SaveChangesAsync(ct);

        return new LinkNewHampshireTollsResult(
            foundTolls,
            notFoundPlazas,
            updatedCount,
            createdCount);
    }
}

