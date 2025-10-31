using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;
using TollService.Infrastructure.Persistence;

namespace TollService.Infrastructure.Services;

public class SpatialQueryService : ISpatialQueryService
{
    private readonly TollDbContext _context;

    public SpatialQueryService(TollDbContext context)
    {
        _context = context;
    }

    public async Task<double?> GetRoadLengthAsync(Guid roadId, CancellationToken ct = default)
    {
        FormattableString sql = $"""
            SELECT ST_LengthSpheroid(
                "Geometry",
                'SPHEROID["WGS 84",6378137,298.257223563]'::spheroid
            ) / 1000.0 AS "Value"
            FROM "Roads"
            WHERE "Id" = {roadId}::uuid
            AND "Geometry" IS NOT NULL
            """;

        var length = await _context.Database
            .SqlQuery<double?>(sql)
            .FirstOrDefaultAsync(ct);

        return length;
    }

    public async Task<double> GetTotalRoadDistanceByStateAsync(string stateCode, CancellationToken ct = default)
    {
        FormattableString sql = $"""
            SELECT COALESCE(
                SUM(ST_LengthSpheroid(
                    "Geometry",
                    'SPHEROID["WGS 84",6378137,298.257223563]'::spheroid
                )) / 1000.0,
                0.0
            ) AS "Value"
            FROM "Roads"
            WHERE "State" = {stateCode}
            AND "Geometry" IS NOT NULL
            """;

        var totalLength = await _context.Database
            .SqlQuery<double>(sql)
            .FirstOrDefaultAsync(ct);

        return totalLength;
    }
}

