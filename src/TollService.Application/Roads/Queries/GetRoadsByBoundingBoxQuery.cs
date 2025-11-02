using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Roads.Queries;

public record GetRoadsByBoundingBoxQuery(
    double MinLatitude, 
    double MinLongitude, 
    double MaxLatitude, 
    double MaxLongitude) : IRequest<List<RoadWithGeometryDto>>;

public class GetRoadsByBoundingBoxQueryHandler(
    ITollDbContext _context) : IRequestHandler<GetRoadsByBoundingBoxQuery, List<RoadWithGeometryDto>>
{
    public async Task<List<RoadWithGeometryDto>> Handle(GetRoadsByBoundingBoxQuery request, CancellationToken ct)
    {
        var boundingBox = new Polygon(new LinearRing(new[]
        {
            new Coordinate(request.MinLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MinLatitude),
            new Coordinate(request.MaxLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MaxLatitude),
            new Coordinate(request.MinLongitude, request.MinLatitude) 
        })) { SRID = 4326 };

        var roads = await _context.Roads
            .Where(r => r.Geometry != null && r.Geometry.Intersects(boundingBox))
            .ToListAsync(ct);

        return roads.Select(road => new RoadWithGeometryDto(
            road.Id,
            road.Name,
            road.Ref,
            road.HighwayType,
            road.IsToll,
            road.Geometry?.Coordinates.Select(c => new PointDto(c.Y, c.X)).ToList() ?? new List<PointDto>()
        )).ToList();
    }
}
