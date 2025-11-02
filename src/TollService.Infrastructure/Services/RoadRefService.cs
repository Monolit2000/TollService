using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Domain;
using TollService.Infrastructure.Persistence;

namespace TollService.Infrastructure.Services;

public class RoadRefService
{
    private readonly TollDbContext _context;
    private const double MaxDistanceMeters = 1; // Максимальное расстояние для сопоставления в метрах

    public RoadRefService(TollDbContext context)
    {
        _context = context;
    }

    public async Task<int> FillMissingRefsAsync(CancellationToken ct = default)
    {
        int iter = 0;
        bool flag = true;
        while (flag)
        {
            // Получаем все дороги без Ref, но с геометрией
            var roadsWithoutRef = await _context.Roads
            .Where(r => r.Ref == null && r.Geometry != null)
            .ToListAsync(ct);

            if (roadsWithoutRef.Count == 0)
                return 0;
            int batchSize = 100; // Обрабатываем по батчам для оптимизации

            int updatedCount = 0;
            for (int i = 0; i < roadsWithoutRef.Count; i += batchSize)
            {
                var batch = roadsWithoutRef.Skip(i).Take(batchSize).ToList();
                int batchUpdated = 0;

                foreach (var roadWithoutRef in batch)
                {
                    if (roadWithoutRef.Geometry == null)
                        continue;

                    var bestMatch = await FindBestRefMatchAsync(roadWithoutRef, ct);

                    if (bestMatch != null)
                    {
                        roadWithoutRef.Ref = bestMatch.Ref;
                        roadWithoutRef.Name = bestMatch.Name;
                        batchUpdated++;
                        updatedCount++;
                        iter++;
                    }
                }

                // Сохраняем изменения после каждого батча, если были обновления
                if (batchUpdated > 0)
                {
                    await _context.SaveChangesAsync(ct);
                }
            }
            if (updatedCount == 0)
                flag = false;

            updatedCount = 0;
        }

        return iter;
    }

    private async Task<Road?> FindBestRefMatchAsync(Road roadWithoutRef, CancellationToken ct)
    {
        if (roadWithoutRef.Geometry == null)
            return null;

        var startPoint = roadWithoutRef.Geometry.StartPoint;
        var endPoint = roadWithoutRef.Geometry.EndPoint;

        // Используем PostGIS для поиска ближайших дорог с Ref
        // Сравниваем оба конца дороги без Ref с линией дорог с Ref
        var startLon = startPoint.X;
        var startLat = startPoint.Y;
        var endLon = endPoint.X;
        var endLat = endPoint.Y;

        FormattableString sql = $"""
            WITH target_points AS (
                SELECT 
                    ST_SetSRID(ST_MakePoint({startLon}, {startLat}), 4326) AS start_pt,
                    ST_SetSRID(ST_MakePoint({endLon}, {endLat}), 4326) AS end_pt
            ),
            candidate_roads AS (
                SELECT 
                    r."Id",
                    r."Ref",
                    r."Geometry"
                FROM "Roads" r
                WHERE r."Ref" IS NOT NULL 
                    AND r."Geometry" IS NOT NULL
            ),
            distances AS (
                SELECT 
                    cr."Id",
                    cr."Ref",
                    LEAST(
                        ST_DistanceSpheroid(
                            tp.start_pt, cr."Geometry",
                            'SPHEROID["WGS 84",6378137,298.257223563]'::spheroid
                        ),
                        ST_DistanceSpheroid(
                            tp.end_pt, cr."Geometry",
                            'SPHEROID["WGS 84",6378137,298.257223563]'::spheroid
                        )
                    ) AS min_distance
                FROM target_points tp
                CROSS JOIN candidate_roads cr
            )
            SELECT 
                d."Id"::uuid AS "Id",
                d."Ref",
                d.min_distance AS "MinDistance"
            FROM distances d
            WHERE d.min_distance <= {MaxDistanceMeters}
            ORDER BY d.min_distance ASC
            LIMIT 1
            """;

        var result = await _context.Database
            .SqlQuery<RoadMatchResult>(sql)
            .FirstOrDefaultAsync(ct);

        if (result == null)
            return null;

        return await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == result.Id, ct);
    }

    private class RoadMatchResult
    {
        public Guid Id { get; set; }
        public string Ref { get; set; } = string.Empty;
        public double MinDistance { get; set; }
    }
}

