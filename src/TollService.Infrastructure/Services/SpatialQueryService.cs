using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        var roadIdParam = new NpgsqlParameter("roadId", roadId);
        
        var length = await _context.Database
            .SqlQueryRaw<double?>("""
                SELECT ST_LengthSpheroid(
                    geometry::geometry,
                    'SPHEROID["WGS 84",6378137,298.257223563]'::spheroid
                ) / 1000.0 as LengthKm
                FROM "Roads"
                WHERE "Id" = @roadId::uuid
                AND geometry IS NOT NULL
                """, roadIdParam)
            .FirstOrDefaultAsync(ct);

        return length;
    }

    public async Task<double> GetTotalRoadDistanceByStateAsync(string stateCode, CancellationToken ct = default)
    {
        var stateParam = new NpgsqlParameter("state", stateCode);
        
        var totalLength = await _context.Database
            .SqlQueryRaw<double>("""
                SELECT COALESCE(
                    SUM(ST_LengthSpheroid(
                        geometry::geometry,
                        'SPHEROID["WGS 84",6378137,298.257223563]'::spheroid
                    )) / 1000.0, 
                    0.0
                ) as TotalLengthKm
                FROM "Roads"
                WHERE "State" = @state
                AND geometry IS NOT NULL
                """, stateParam)
            .FirstOrDefaultAsync(ct);

        return totalLength;
    }
}

