using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NJ;

public record ParkwayTollPlaza(
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string? Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("milepost")] double? Milepost,
    [property: System.Text.Json.Serialization.JsonPropertyName("directions")] string? Directions,
    [property: System.Text.Json.Serialization.JsonPropertyName("rates")] ParkwayRates? Rates);

public record ParkwayRates(
    [property: System.Text.Json.Serialization.JsonPropertyName("cash")] double? Cash,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass_peak")] double? EzPassPeak,
    [property: System.Text.Json.Serialization.JsonPropertyName("ez_pass_off_peak_truck")] double? EzPassOffPeakTruck);

public record ParkwayPricesData(
    [property: System.Text.Json.Serialization.JsonPropertyName("road_name")] string? RoadName,
    [property: System.Text.Json.Serialization.JsonPropertyName("year")] string? Year,
    [property: System.Text.Json.Serialization.JsonPropertyName("vehicle_class")] string? VehicleClass,
    [property: System.Text.Json.Serialization.JsonPropertyName("axles")] int? Axles,
    [property: System.Text.Json.Serialization.JsonPropertyName("description")] string? Description,
    [property: System.Text.Json.Serialization.JsonPropertyName("toll_plazas")] List<ParkwayTollPlaza>? TollPlazas);

public record LinkParkwayPricesCommand(string JsonPayload)
    : IRequest<LinkParkwayPricesResult>;

public record LinkedTollInfo(
    string PlazaName,
    Guid TollId,
    string? TollName,
    decimal? OldPrice,
    decimal? NewPrice);

public record LinkParkwayPricesResult(
    List<LinkedTollInfo> LinkedTolls,
    List<string> NotFoundPlazas,
    int UpdatedCount,
    string? Error = null);

public class LinkParkwayPricesCommandHandler(
    ITollDbContext _context) : IRequestHandler<LinkParkwayPricesCommand, LinkParkwayPricesResult>
{
    // New Jersey bounds: (south, west, north, east) = (38.9, -75.6, 41.4, -73.9)
    private static readonly double NjMinLatitude = 38.9;
    private static readonly double NjMinLongitude = -75.6;
    private static readonly double NjMaxLatitude = 41.4;
    private static readonly double NjMaxLongitude = -73.9;

    public async Task<LinkParkwayPricesResult> Handle(LinkParkwayPricesCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JsonPayload))
        {
            return new LinkParkwayPricesResult(new(), new(), 0, "JSON payload is empty");
        }

        ParkwayPricesData? data;
        try
        {
            await Task.Yield();
            data = JsonSerializer.Deserialize<ParkwayPricesData>(request.JsonPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException jsonEx)
        {
            return new LinkParkwayPricesResult(new(), new(), 0, $"Ошибка парсинга JSON: {jsonEx.Message}");
        }

        if (data?.TollPlazas == null || data.TollPlazas.Count == 0)
        {
            return new LinkParkwayPricesResult(new(), new(), 0, "Плазы не найдены в ответе");
        }

        // Создаем bounding box для New Jersey
        var njBoundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(NjMinLongitude, NjMinLatitude),
            new Coordinate(NjMaxLongitude, NjMinLatitude),
            new Coordinate(NjMaxLongitude, NjMaxLatitude),
            new Coordinate(NjMinLongitude, NjMaxLatitude),
            new Coordinate(NjMinLongitude, NjMinLatitude)
        }))
        { SRID = 4326 };

        var linkedTolls = new List<LinkedTollInfo>();
        var notFoundPlazas = new List<string>();
        int updatedCount = 0;

        foreach (var plaza in data.TollPlazas)
        {
            if (string.IsNullOrWhiteSpace(plaza.Name))
            {
                notFoundPlazas.Add("Plaza with empty name");
                continue;
            }

            // Ищем ВСЕ tolls по имени в пределах New Jersey
            // Сначала ищем точное совпадение (без учета регистра через ToLower)
            var plazaNameLower = plaza.Name.ToLower();
            var tolls = await _context.Tolls
                .Where(t =>
                    t.Location != null &&
                    njBoundingBox.Contains(t.Location) &&
                    t.Name != null &&
                    t.Name.ToLower() == plazaNameLower)
                .ToListAsync(ct);

            // Если не нашли точное совпадение, ищем по части имени (учитывая возможные суффиксы типа NB, SB)
            if (tolls.Count == 0)
            {
                // Пробуем найти по основной части имени (без суффиксов NB, SB, etc.)
                var nameWithoutSuffix = plaza.Name.TrimEnd();
                var commonSuffixes = new[] { " NB", " SB", " NX", " SX", " NE", " SE" };
                foreach (var suffix in commonSuffixes)
                {
                    if (nameWithoutSuffix.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        nameWithoutSuffix = nameWithoutSuffix.Substring(0, nameWithoutSuffix.Length - suffix.Length).Trim();
                        break;
                    }
                }

                var nameWithoutSuffixLower = nameWithoutSuffix.ToLower();
                var plazaNameLowerForContains = plaza.Name.ToLower();
                
                // Загружаем все tolls в пределах bounding box и фильтруем в памяти
                var allTollsInBox = await _context.Tolls
                    .Where(t =>
                        t.Location != null &&
                        njBoundingBox.Contains(t.Location) &&
                        t.Name != null)
                    .ToListAsync(ct);

                tolls = allTollsInBox
                    .Where(t => 
                        t.Name!.ToLower().Contains(plazaNameLowerForContains) ||
                        t.Name!.ToLower().Contains(nameWithoutSuffixLower) ||
                        plazaNameLowerForContains.Contains(t.Name.ToLower()))
                    .ToList();
            }

            if (tolls.Count == 0)
            {
                notFoundPlazas.Add(plaza.Name);
                continue;
            }

            // Определяем цены из rates
            decimal? price = null;
            double? iPassPrice = null;
            if (plaza.Rates != null)
            {
                // Используем cash для Price и PayOnline
                if (plaza.Rates.Cash.HasValue)
                {
                    price = (decimal)plaza.Rates.Cash.Value;
                }
                else if (plaza.Rates.EzPassPeak.HasValue)
                {
                    price = (decimal)plaza.Rates.EzPassPeak.Value;
                }

                // Используем ez_pass_peak для IPass
                if (plaza.Rates.EzPassPeak.HasValue)
                {
                    iPassPrice = plaza.Rates.EzPassPeak.Value;
                }
                else if (plaza.Rates.EzPassOffPeakTruck.HasValue)
                {
                    iPassPrice = plaza.Rates.EzPassOffPeakTruck.Value;
                }
            }

            // Устанавливаем цены для всех найденных tolls
            foreach (var toll in tolls)
            {
                var oldPrice = toll.Price;
                
                if (price.HasValue)
                {
                    toll.Price = price.Value;
                    toll.PayOnline = (double)price.Value;
                }

                if (iPassPrice.HasValue)
                {
                    toll.IPass = iPassPrice.Value;
                }

                linkedTolls.Add(new LinkedTollInfo(
                    PlazaName: plaza.Name,
                    TollId: toll.Id,
                    TollName: toll.Name,
                    OldPrice: oldPrice,
                    NewPrice: price));

                updatedCount++;
            }
        }

         await _context.SaveChangesAsync(ct);

        return new LinkParkwayPricesResult(
            linkedTolls,
            notFoundPlazas,
            updatedCount);
    }
}

