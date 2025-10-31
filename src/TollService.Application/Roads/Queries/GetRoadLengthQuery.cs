using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Infrastructure.Persistence;

namespace TollService.Application.Roads.Queries;

public record GetRoadLengthQuery(Guid RoadId) : IRequest<double?>;

public class GetRoadLengthQueryHandler : IRequestHandler<GetRoadLengthQuery, double?>
{
    private readonly TollDbContext _context;

    public GetRoadLengthQueryHandler(TollDbContext context)
    {
        _context = context;
    }

    public async Task<double?> Handle(GetRoadLengthQuery request, CancellationToken ct)
    {
        // Используем raw SQL для точного вычисления длины через PostGIS
        // ST_LengthSpheroid вычисляет геодезическое расстояние в метрах для WGS84
        var length = await _context.Database
            .SqlQuery<double?>($"""
                SELECT ST_LengthSpheroid(
                    geometry::geometry,
                    'SPHEROID[""WGS 84"",6378137,298.257223563]'::spheroid
                ) / 1000.0 as LengthKm
                FROM "Roads"
                WHERE "Id" = {request.RoadId}::uuid
                AND geometry IS NOT NULL
                """)
            .FirstOrDefaultAsync(ct);

        return length;
    }
}

