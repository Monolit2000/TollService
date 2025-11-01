using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadNamesByStateQuery(string State) : IRequest<List<string>>;

public class GetRoadNamesByStateQueryHandler(
    ITollDbContext _context) : IRequestHandler<GetRoadNamesByStateQuery, List<string>>
{
    public async Task<List<string>> Handle(GetRoadNamesByStateQuery request, CancellationToken ct)
    {
        return await _context.Roads
            .Where(r => r.State == request.State && !string.IsNullOrEmpty(r.Ref))
            .Select(r => $"{r.Ref} {r.Name}")
            .Distinct()
            .ToListAsync(ct);
    }
}


