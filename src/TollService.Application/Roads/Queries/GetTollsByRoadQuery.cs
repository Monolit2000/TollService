using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Contracts;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetTollsByRoadQuery(Guid RoadId) : IRequest<List<TollDto>>;

public class GetTollsByRoadQueryHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<GetTollsByRoadQuery, List<TollDto>>
{
    public async Task<List<TollDto>> Handle(GetTollsByRoadQuery request, CancellationToken ct)
    {
        var tolls = await _context.Tolls.AsNoTracking().Where(t => t.RoadId == request.RoadId).ToListAsync(ct);
        return _mapper.Map<List<TollDto>>(tolls);
    }
}


