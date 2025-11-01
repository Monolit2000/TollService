using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Contracts;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadByIdQuery(Guid Id) : IRequest<RoadDto?>;

public class GetRoadByIdQueryHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<GetRoadByIdQuery, RoadDto?>
{
    public async Task<RoadDto?> Handle(GetRoadByIdQuery request, CancellationToken ct)
    {
        var road = await _context.Roads.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.Id, ct);
        return road == null ? null : _mapper.Map<RoadDto>(road);
    }
}


