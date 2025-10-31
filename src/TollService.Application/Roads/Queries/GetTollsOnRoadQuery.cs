using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Roads.Queries;

public record GetTollsOnRoadQuery(Guid RoadId) : IRequest<List<TollDto>>;

public class GetTollsOnRoadQueryHandler : IRequestHandler<GetTollsOnRoadQuery, List<TollDto>>
{
    private readonly ITollDbContext _context;
    private readonly IMapper _mapper;

    public GetTollsOnRoadQueryHandler(ITollDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<TollDto>> Handle(GetTollsOnRoadQuery request, CancellationToken ct)
    {
        var road = await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == request.RoadId, ct);

        if (road == null || road.Geometry == null)
            return new List<TollDto>();

        // Находим все платные пункты, которые находятся на этой дороге (в пределах 100 метров)
        var tolls = await _context.Tolls
            .Where(t => t.Location != null && 
                       t.Location.IsWithinDistance(road.Geometry, 100))
            .ToListAsync(ct);

        return _mapper.Map<List<TollDto>>(tolls);
    }
}

