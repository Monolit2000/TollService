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

public record FoundTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    string? TollKey,
    string? TollNumber);

public record LinkNewHampshireTollsResult(
    List<FoundTollInfo> FoundTolls,
    List<string> NotFoundPlazas,
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
            return new LinkNewHampshireTollsResult(new(), new(), "JSON payload is empty");
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
                return new LinkNewHampshireTollsResult(new(), new(), $"Ошибка парсинга JSON: {jsonEx.Message}. Убедитесь, что JSON содержит поле 'new_hampshire_turnpike_system_tolls' с массивом 'toll_plazas'.");
            }
        }

        if (data?.NewHampshireTurnpikeSystemTolls?.TollPlazas == null || data.NewHampshireTurnpikeSystemTolls.TollPlazas.Count == 0)
        {
            // Проверяем, что вообще было распарсено
            if (data == null)
            {
                return new LinkNewHampshireTollsResult(new(), new(), "Не удалось распарсить JSON. Проверьте структуру данных.");
            }
            if (data.NewHampshireTurnpikeSystemTolls == null)
            {
                return new LinkNewHampshireTollsResult(new(), new(), "Поле 'new_hampshire_turnpike_system_tolls' не найдено в JSON.");
            }
            return new LinkNewHampshireTollsResult(new(), new(), "Плазы не найдены в ответе (массив 'toll_plazas' пуст или отсутствует).");
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

        var foundTolls = new List<FoundTollInfo>();
        var notFoundPlazas = new List<string>();

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

            // Добавляем все найденные tolls в результат
            foreach (var toll in tolls)
            {
                foundTolls.Add(new FoundTollInfo(
                    PlazaName: plaza.Name,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    TollKey: toll.Key,
                    TollNumber: toll.Number));
            }
        }

        return new LinkNewHampshireTollsResult(
            foundTolls,
            notFoundPlazas);
    }
}

