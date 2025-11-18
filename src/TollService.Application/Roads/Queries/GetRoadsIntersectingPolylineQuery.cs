using MediatR;
using TollService.Contracts;
using NetTopologySuite.Geometries;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadsIntersectingPolylineQuery(
    List<List<double>> Coordinates) : IRequest<List<RoadWithGeometryDto>>;

public class GetRoadsIntersectingPolylineQueryHandler(
    ITollDbContext _context) : IRequestHandler<GetRoadsIntersectingPolylineQuery, List<RoadWithGeometryDto>>
{
    public async Task<List<RoadWithGeometryDto>> Handle(GetRoadsIntersectingPolylineQuery request, CancellationToken ct)
    {
        if (request.Coordinates == null || request.Coordinates.Count < 2)
        {
            return new List<RoadWithGeometryDto>();
        }

        var coordinates = request.Coordinates
            .Where(c => c != null && c.Count >= 2)
            .Select(c => new Coordinate(c[1], c[0])) 
            .ToArray();

        if (coordinates.Length < 2)
        {
            return new List<RoadWithGeometryDto>();
        }

        var polyline = new LineString(coordinates) { SRID = 4326 };

        var minLongitude = coordinates.Min(c => c.X);
        var maxLongitude = coordinates.Max(c => c.X);
        var minLatitude = coordinates.Min(c => c.Y);
        var maxLatitude = coordinates.Max(c => c.Y);

        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minLongitude, minLatitude),
            new Coordinate(maxLongitude, minLatitude),
            new Coordinate(maxLongitude, maxLatitude),
            new Coordinate(minLongitude, maxLatitude),
            new Coordinate(minLongitude, minLatitude)
        }))
        { SRID = 4326 };

        // 2. Находим дороги, пересекающие полилинию (с предварительной фильтрацией по bbox)
        var intersectingRoads = await _context.Roads
            .Where(r => r.Geometry != null &&
                        r.Geometry.Intersects(boundingBox) &&
                        r.Geometry.Intersects(polyline))
            .ToListAsync(ct);

        if (!intersectingRoads.Any())
            return new List<RoadWithGeometryDto>();

        // 3. Собираем все Ref и Name из найденных дорог (игнорируем null/пустые)
        var refs = intersectingRoads
            .Where(r => !string.IsNullOrWhiteSpace(r.Ref))
            .Select(r => r.Ref!.Trim())
            .Distinct()
            .ToList();

        var names = intersectingRoads
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Name!.Trim())
            .Distinct()
            .ToList();

        // 4. Ищем дополнительные дороги по тем же Ref/Name + в том же bbox
        var additionalRoads = await _context.Roads
            .Where(r => r.Geometry != null &&
                        r.Geometry.Intersects(boundingBox) &&
                        (refs.Contains(r.Ref!) || names.Contains(r.Name!)))
            .ToListAsync(ct);

        // 5. Объединяем, исключаем дубли по WayId (предполагается, что поле называется WayId)
        var allRoads = intersectingRoads
            .Concat(additionalRoads)
            .GroupBy(r => r.WayId)           // <-- здесь твой уникальный идентификатор OSM-объекта
            .Select(g => g.First())          // берём только одну запись на WayId
            .ToList();

        // 6. Маппинг в DTO
        return allRoads.Select(r => new RoadWithGeometryDto(
            r.Id,
            r.Name,
            r.Ref,
            r.HighwayType,
            r.IsToll,
            r.Geometry?.Coordinates?
                .Select(c => new PointDto(c.Y, c.X))
                .ToList() ?? new List<PointDto>()
        )).ToList();
    }
}