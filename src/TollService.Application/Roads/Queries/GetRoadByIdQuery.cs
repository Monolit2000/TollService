using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Contracts;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadByIdQuery(Guid Id) : IRequest<RoadDto?>;

public class GetRoadByIdQueryHandler : IRequestHandler<GetRoadByIdQuery, RoadDto?>
{
    private readonly ITollDbContext _context;
    private readonly IMapper _mapper;

    public GetRoadByIdQueryHandler(ITollDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<RoadDto?> Handle(GetRoadByIdQuery request, CancellationToken ct)
    {
        var road = await _context.Roads.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.Id, ct);
        return road == null ? null : _mapper.Map<RoadDto>(road);
    }
}


