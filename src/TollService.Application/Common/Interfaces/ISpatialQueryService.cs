namespace TollService.Application.Common.Interfaces;

public interface ISpatialQueryService
{
    Task<double?> GetRoadLengthAsync(Guid roadId, CancellationToken ct = default);
    Task<double> GetTotalRoadDistanceByStateAsync(string stateCode, CancellationToken ct = default);
}

