using MediatR;
using Microsoft.EntityFrameworkCore;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetTotalRoadLengthByStateQuery(string State) : IRequest<double>;

public class GetTotalRoadLengthByStateQueryHandler : IRequestHandler<GetTotalRoadLengthByStateQuery, double>
{
    private readonly ITollDbContext _context;

    public GetTotalRoadLengthByStateQueryHandler(ITollDbContext context)
    {
        _context = context;
    }

    public async Task<double> Handle(GetTotalRoadLengthByStateQuery request, CancellationToken ct)
    {
        // Используем raw SQL для точного вычисления общей длины дорог через PostGIS
        // ST_LengthSpheroid вычисляет геодезическое расстояние в метрах для WGS84
        // Суммируем длину всех дорог в штате и конвертируем в километры
        var totalLength = await _context.Database
            .SqlQuery<double>($"""
                SELECT COALESCE(
                    SUM(ST_LengthSpheroid(
                        geometry::geometry,
                        'SPHEROID[""WGS 84"",6378137,298.257223563]'::spheroid
                    )) / 1000.0,
                    0.0
                ) as TotalLengthKm
                FROM "Roads"
                WHERE "State" = {request.State}
                AND geometry IS NOT NULL
                """)
            .FirstOrDefaultAsync(ct);

        return totalLength;
    }
}

