using MediatR;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetTotalRoadDistanceByStateQuery(string StateCode) : IRequest<double>;

public class GetTotalRoadDistanceByStateQueryHandler(
    ISpatialQueryService _spatialQueryService) : IRequestHandler<GetTotalRoadDistanceByStateQuery, double>
{
    public async Task<double> Handle(GetTotalRoadDistanceByStateQuery request, CancellationToken ct)
    {
        return await _spatialQueryService.GetTotalRoadDistanceByStateAsync(request.StateCode, ct);
    }
}

