using MediatR;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetTotalRoadDistanceByStateQuery(string StateCode) : IRequest<double>;

public class GetTotalRoadDistanceByStateQueryHandler : IRequestHandler<GetTotalRoadDistanceByStateQuery, double>
{
    private readonly ISpatialQueryService _spatialQueryService;

    public GetTotalRoadDistanceByStateQueryHandler(ISpatialQueryService spatialQueryService)
    {
        _spatialQueryService = spatialQueryService;
    }

    public async Task<double> Handle(GetTotalRoadDistanceByStateQuery request, CancellationToken ct)
    {
        return await _spatialQueryService.GetTotalRoadDistanceByStateAsync(request.StateCode, ct);
    }
}

