using AutoMapper;
using MediatR;
using NetTopologySuite.Geometries;
using TollService.Contracts;
using TollService.Domain;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Commands;

public record AddRoadCommand(string Name, string HighwayType, bool IsToll, List<(double Latitude, double Longitude)>? Coordinates = null) : IRequest<RoadDto>;

public class AddRoadCommandHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<AddRoadCommand, RoadDto>
{
    public async Task<RoadDto> Handle(AddRoadCommand request, CancellationToken ct)
    {
        LineString? geometry = null;
        
        if (request.Coordinates != null && request.Coordinates.Count >= 2)
        {
            var coords = request.Coordinates
                .Select(c => new Coordinate(c.Longitude, c.Latitude))
                .ToArray();
            geometry = new LineString(coords) { SRID = 4326 };
        }

        var road = new Road
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            HighwayType = request.HighwayType,
            IsToll = request.IsToll,
            Geometry = geometry
        };

        _context.Roads.Add(road);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<RoadDto>(road);
    }
}


