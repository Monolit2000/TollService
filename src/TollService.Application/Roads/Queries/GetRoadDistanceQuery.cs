using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadDistanceQuery(Guid RoadId, double Latitude, double Longitude) : IRequest<double?>;

public class GetRoadDistanceQueryHandler(
    ITollDbContext _context) : IRequestHandler<GetRoadDistanceQuery, double?>
{
    public async Task<double?> Handle(GetRoadDistanceQuery request, CancellationToken ct)
    {
        var road = await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == request.RoadId, ct);

        if (road?.Geometry == null)
            return null;

        var point = new Point(request.Longitude, request.Latitude) { SRID = 4326 };
        
        if (road.Geometry.Distance(point) * 111320 > 1000) 
            return null;

        return road.Geometry.Distance(point) * 111320; 
    }
}

