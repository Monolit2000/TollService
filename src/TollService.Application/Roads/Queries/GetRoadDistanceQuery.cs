using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;

namespace TollService.Application.Roads.Queries;

public record GetRoadDistanceQuery(Guid RoadId, double Latitude, double Longitude) : IRequest<double?>;

public class GetRoadDistanceQueryHandler : IRequestHandler<GetRoadDistanceQuery, double?>
{
    private readonly ITollDbContext _context;

    public GetRoadDistanceQueryHandler(ITollDbContext context)
    {
        _context = context;
    }

    public async Task<double?> Handle(GetRoadDistanceQuery request, CancellationToken ct)
    {
        var road = await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == request.RoadId, ct);

        if (road?.Geometry == null)
            return null;

        var point = new Point(request.Longitude, request.Latitude) { SRID = 4326 };

        // Используем PostGIS функцию ST_Distance для вычисления расстояния
        // В метрах (используя геодезический расчет для WGS84)
        // EF Core преобразует IsWithinDistance в ST_DWithin, но для точного расстояния нужен raw SQL
        
        // Временное решение через NetTopologySuite (менее точное, но работает без raw SQL)
        // Для точного геодезического расстояния лучше использовать raw SQL:
        // SELECT ST_Distance(
        //     ST_Transform(geometry, 3857), 
        //     ST_Transform(ST_SetSRID(ST_MakePoint(lon, lat), 4326), 3857)
        // )
        
        // Простое приближение через проекцию
        if (road.Geometry.Distance(point) * 111320 > 1000) // Конвертация градусов в метры (приблизительно)
            return null;

        // Более точный расчет через PostGIS функции требует raw SQL
        // Возвращаем приблизительное расстояние в метрах
        return road.Geometry.Distance(point) * 111320; // ~111.32 км на градус на экваторе
    }
}

