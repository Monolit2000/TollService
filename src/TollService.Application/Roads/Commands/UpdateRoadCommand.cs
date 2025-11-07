using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Roads.Commands;

public record UpdateRoadCommand(
    Guid Id,
    string? Name = null,
    string? HighwayType = null,
    bool? IsToll = null,
    string? State = null,
    string? Ref = null,
    long? WayId = null,
    List<PointDto>? Coordinates = null) : IRequest<RoadDto?>;

public class UpdateRoadCommandHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<UpdateRoadCommand, RoadDto?>
{
    public async Task<RoadDto?> Handle(UpdateRoadCommand request, CancellationToken ct)
    {
        var road = await _context.Roads.FirstOrDefaultAsync(r => r.Id == request.Id, ct);
        
        if (road == null)
            return null;

        if (request.Name != null)
            road.Name = request.Name;

        if (request.HighwayType != null)
            road.HighwayType = request.HighwayType;

        if (request.IsToll.HasValue)
            road.IsToll = request.IsToll.Value;

        if (request.State != null)
            road.State = request.State;

        if (request.Ref != null)
            road.Ref = request.Ref;

        if (request.WayId.HasValue)
            road.WayId = request.WayId.Value;

        if (request.Coordinates != null)
        {
            if (request.Coordinates.Count >= 2)
            {
                var coords = request.Coordinates
                    .Select(c => new Coordinate(c.Longitude, c.Latitude))
                    .ToArray();
                road.Geometry = new LineString(coords) { SRID = 4326 };
            }
            else if (request.Coordinates.Count == 0)
            {
                road.Geometry = null;
            }
        }

        await _context.SaveChangesAsync(ct);

        return _mapper.Map<RoadDto>(road);
    }
}

