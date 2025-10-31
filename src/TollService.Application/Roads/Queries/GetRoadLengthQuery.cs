using MediatR;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadLengthQuery(Guid RoadId) : IRequest<double?>;

public class GetRoadLengthQueryHandler : IRequestHandler<GetRoadLengthQuery, double?>
{
    private readonly ISpatialQueryService _spatialQueryService;

    public GetRoadLengthQueryHandler(ISpatialQueryService spatialQueryService)
    {
        _spatialQueryService = spatialQueryService;
    }

    public async Task<double?> Handle(GetRoadLengthQuery request, CancellationToken ct)
    {
        return await _spatialQueryService.GetRoadLengthAsync(request.RoadId, ct);
    }
}

