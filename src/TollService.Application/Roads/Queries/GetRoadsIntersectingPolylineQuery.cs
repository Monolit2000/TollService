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
            .Select(c => new Coordinate(c[0], c[1])) 
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

        var roads = await _context.Roads
            .Where(r => r.Geometry != null &&
                   r.Geometry.Intersects(boundingBox) &&
                   r.Geometry.Intersects(polyline))
            .ToListAsync(ct);

        return roads.Select(r => new RoadWithGeometryDto(
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
        )).ToList();
    }
}

