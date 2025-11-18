using MediatR;
using TollService.Contracts;
using NetTopologySuite.Geometries;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

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

        // Создаем LineString из координат
        // Ожидаемый формат: [[longitude, latitude], [longitude, latitude], ...]
        var coordinates = request.Coordinates
            .Where(c => c != null && c.Count >= 2)
            .Select(c => new Coordinate(c[1], c[0])) // [0] = longitude, [1] = latitude
            .ToArray();

        if (coordinates.Length < 2)
        {
            return new List<RoadWithGeometryDto>();
        }

        var polyline = new LineString(coordinates) { SRID = 4326 };

        // Вычисляем bounding box полилайна для предварительной фильтрации
        var minLongitude = coordinates.Min(c => c.X);
        var maxLongitude = coordinates.Max(c => c.X);
        var minLatitude = coordinates.Min(c => c.Y);
        var maxLatitude = coordinates.Max(c => c.Y);

        // Создаем bounding box для фильтрации
        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minLongitude, minLatitude),
            new Coordinate(maxLongitude, minLatitude),
            new Coordinate(maxLongitude, maxLatitude),
            new Coordinate(minLongitude, maxLatitude),
            new Coordinate(minLongitude, minLatitude)
        }))
        { SRID = 4326 };

        // Сначала фильтруем дороги по bounding box, затем проверяем пересечение с полилайном
        var candidateRoads = await _context.Roads
            .AsNoTracking()
            .Where(r => r.Geometry != null &&
                        r.Geometry.Intersects(boundingBox))
            .ToListAsync(ct);

        var roadsIntersectingPolyline = candidateRoads
            .Where(r => r.Geometry != null && r.Geometry.Intersects(polyline))
            .ToList();

        if (roadsIntersectingPolyline.Count == 0)
        {
            return new List<RoadWithGeometryDto>();
        }

        var relatedRoads = ExpandRoadIntersections(roadsIntersectingPolyline, candidateRoads);

        return relatedRoads
            .Select(r => new RoadWithGeometryDto(
            r.Id,
            r.Name,
            r.Ref,
            r.HighwayType,
            r.IsToll,
            r.Geometry != null && r.Geometry.Coordinates != null && r.Geometry.Coordinates.Length > 0
                ? r.Geometry.Coordinates
                    .Where(c => c != null)
                    .Select(c => new PointDto(c.Y, c.X))
                    .ToList()
                : new List<PointDto>()
        ))
        .ToList();
    }

    private static List<Road> ExpandRoadIntersections(List<Road> seedRoads, List<Road> candidates)
    {
        var result = new Dictionary<Guid, Road>();
        var queue = new Queue<Road>();

        foreach (var road in seedRoads)
        {
            if (road.Geometry == null)
            {
                continue;
            }

            if (result.TryAdd(road.Id, road))
            {
                queue.Enqueue(road);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var candidate in candidates)
            {
                if (candidate.Geometry == null)
                {
                    continue;
                }

                if (result.ContainsKey(candidate.Id))
                {
                    continue;
                }

                if (current.Geometry!.Intersects(candidate.Geometry))
                {
                    result.Add(candidate.Id, candidate);
                    queue.Enqueue(candidate);
                }
            }
        }

        return result.Values.ToList();
    }
}

