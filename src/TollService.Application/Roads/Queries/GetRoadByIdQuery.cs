using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Contracts;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadByIdQuery(Guid Id) : IRequest<RoadWithGeometryDto?>;

public class GetRoadByIdQueryHandler(
    ITollDbContext _context) : IRequestHandler<GetRoadByIdQuery, RoadWithGeometryDto?>
{
    public async Task<RoadWithGeometryDto?> Handle(GetRoadByIdQuery request, CancellationToken ct)
    {
        var road = await _context.Roads
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct);
        
        if (road == null)
            return null;

        var coordinates = new List<PointDto>();
        
        if (road.Geometry != null && road.Geometry.Coordinates != null && road.Geometry.Coordinates.Length > 0)
        {
            coordinates = road.Geometry.Coordinates
                .Where(c => c != null)
                .Select(c => new PointDto(c.Y, c.X))
                .ToList();
        }

        return new RoadWithGeometryDto(
            road.Id,
            road.Name,
            road.Ref,
            road.HighwayType,
            road.IsToll,
            coordinates
        );
    }
}


