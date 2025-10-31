using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Roads.Queries;

public record GetRoadsNearPointQuery(double Latitude, double Longitude, double RadiusMeters) : IRequest<List<RoadDto>>;

public class GetRoadsNearPointQueryHandler : IRequestHandler<GetRoadsNearPointQuery, List<RoadDto>>
{
    private readonly ITollDbContext _context;
    private readonly IMapper _mapper;

    public GetRoadsNearPointQueryHandler(ITollDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<RoadDto>> Handle(GetRoadsNearPointQuery request, CancellationToken ct)
    {
        var point = new Point(request.Longitude, request.Latitude) { SRID = 4326 };

        var roads = await _context.Roads
            .Where(r => r.Geometry != null && r.Geometry.IsWithinDistance(point, request.RadiusMeters))
            .ToListAsync(ct);

        return _mapper.Map<List<RoadDto>>(roads);
    }
}

