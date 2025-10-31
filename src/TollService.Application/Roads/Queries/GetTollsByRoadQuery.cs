using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Contracts;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetTollsByRoadQuery(Guid RoadId) : IRequest<List<TollDto>>;

public class GetTollsByRoadQueryHandler : IRequestHandler<GetTollsByRoadQuery, List<TollDto>>
{
    private readonly ITollDbContext _context;
    private readonly IMapper _mapper;

    public GetTollsByRoadQueryHandler(ITollDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<TollDto>> Handle(GetTollsByRoadQuery request, CancellationToken ct)
    {
        var tolls = await _context.Tolls.AsNoTracking().Where(t => t.RoadId == request.RoadId).ToListAsync(ct);
        return _mapper.Map<List<TollDto>>(tolls);
    }
}


