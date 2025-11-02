using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Roads.Queries;

public record GetRoadsByBoundingBoxQuery(
    double MinLatitude,
    double MinLongitude,
    double MaxLatitude,
    double MaxLongitude) : IRequest<List<RoadWithGeometryDto>>;

public class GetRoadsByBoundingBoxQueryHandler(
    ITollDbContext _context) : IRequestHandler<GetRoadsByBoundingBoxQuery, List<RoadWithGeometryDto>>
{
    private const double MergeToleranceMeters = 1.0;

    public async Task<List<RoadWithGeometryDto>> Handle(GetRoadsByBoundingBoxQuery request, CancellationToken ct)
    {
        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(request.MinLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MinLatitude)
        }))
        { SRID = 4326 };

        var roads = await _context.Roads
            .Where(r => r.Geometry != null && r.Geometry.Intersects(boundingBox))
            .ToListAsync(ct);

        var merged = roads;
        List<(LineString Geometry, string Name, string Ref, string HighwayType, bool IsToll)> lastMerged;
        do
        {
            lastMerged = MergeConnectedRoads(merged);
            // превращаем результат обратно в Road, чтобы можно было повторно объединить
            merged = lastMerged.Select(m => new Road
            {
                Id = Guid.NewGuid(),
                Name = m.Name,
                Ref = m.Ref,
                HighwayType = m.HighwayType,
                IsToll = m.IsToll,
                Geometry = m.Geometry
            }).ToList();
        }
        while (lastMerged.Count < merged.Count); // продолжаем, пока происходит объединение

        return merged.Select(m => new RoadWithGeometryDto(
            Guid.NewGuid(),
            m.Name,
            m.Ref,
            m.HighwayType,
            m.IsToll,
            m.Geometry.Coordinates.Select(c => new PointDto(c.Y, c.X)).ToList()
        )).ToList();
    }

    private List<(LineString Geometry, string Name, string Ref, string HighwayType, bool IsToll)> MergeConnectedRoads(List<Road> roads)
    {
        var result = new List<(LineString, string, string, string, bool)>();
        var visited = new HashSet<Guid>();

        // Группируем дороги по Ref, чтобы обрабатывать только дороги с одинаковым Ref
        var groups = roads
            .GroupBy(r => r.Ref ?? $"__no_ref__{Guid.NewGuid()}")
            .ToList();

        foreach (var group in groups)
        {
            var roadsInGroup = group.OrderBy(r => r.Id).ToList(); // Сортировка для детерминированного результата
            var merger = new LineMerger();
            var toMerge = new List<Road>();
            var name = "";
            var highwayType = "";
            var isToll = false;

            // Собираем все дороги в группе, которые физически связаны
            foreach (var road in roadsInGroup)
            {
                if (visited.Contains(road.Id))
                    continue;

                toMerge.Add(road);
                visited.Add(road.Id);
                var queue = new Queue<Road>();
                queue.Enqueue(road);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var endPoints = new[]
                    {
                    current.Geometry!.StartPoint,
                    current.Geometry.EndPoint
                };

                    foreach (var other in roadsInGroup)
                    {
                        if (visited.Contains(other.Id) || other.Id == current.Id)
                            continue;

                        var otherPoints = new[]
                        {
                        other.Geometry!.StartPoint,
                        other.Geometry.EndPoint
                    };

                        if (endPoints.Any(p1 => otherPoints.Any(p2 => p1.Distance(p2) <= MetersToDegrees(MergeToleranceMeters))))
                        {
                            queue.Enqueue(other);
                            toMerge.Add(other);
                            visited.Add(other.Id);
                        }
                    }
                }
            }

            if (!toMerge.Any())
                continue;

            // Объединяем геометрии
            foreach (var road in toMerge)
            {
                merger.Add(road.Geometry);
            }

            // Получаем объединённую геометрию
            var mergedGeometries = merger.GetMergedLineStrings();
            if (!mergedGeometries.Any())
                continue;

            // Берём первый объединённый полилайн (или единственный, если всё связано)
            var mergedGeometry = (LineString)mergedGeometries.First();

            // Определяем атрибуты объединённой дороги
            name = toMerge.FirstOrDefault(r => !string.IsNullOrEmpty(r.Name))?.Name ?? "";
            highwayType = toMerge.FirstOrDefault(r => !string.IsNullOrEmpty(r.HighwayType))?.HighwayType ?? "";
            isToll = toMerge.Any(r => r.IsToll);

            result.Add((mergedGeometry, name, group.Key, highwayType, isToll));
        }

        return result;
    }

    private static double MetersToDegrees(double meters)
    {
        // простое приближение: 1° ≈ 111.32 км
        return meters / 111_320.0;
    }
}
