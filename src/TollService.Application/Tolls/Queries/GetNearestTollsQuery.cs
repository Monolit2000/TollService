using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Tolls.Queries;

public record GetNearestTollsQuery(double Latitude, double Longitude, double RadiusKm) : IRequest<List<TollDto>>;

public class GetNearestTollsQueryHandler : IRequestHandler<GetNearestTollsQuery, List<TollDto>>
{
    private readonly ITollDbContext _context;
    private readonly IMapper _mapper;

    public GetNearestTollsQueryHandler(ITollDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<TollDto>> Handle(GetNearestTollsQuery request, CancellationToken ct)
    {
        var point = new Point(request.Longitude, request.Latitude) { SRID = 4326 };

        var tolls = await _context.Tolls
            .Where(t => t.Location != null && t.Location.IsWithinDistance(point, request.RadiusKm * 1000))
            .ToListAsync(ct);

        return _mapper.Map<List<TollDto>>(tolls);
    }
}




