using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Tolls.Queries;

public record GetTollsAlongPolylineQuery(
    List<List<double>> Coordinates, 
    double DistanceMeters = 1) : IRequest<List<TollDto>>;

public class GetTollsAlongPolylineQueryHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<GetTollsAlongPolylineQuery, List<TollDto>>
{
    public async Task<List<TollDto>> Handle(GetTollsAlongPolylineQuery request, CancellationToken ct)
    {
        if (request.Coordinates == null || request.Coordinates.Count < 2)
        {
            return new List<TollDto>();
        }

        // Создаем LineString из координат
        // Ожидаемый формат: [[longitude, latitude], [longitude, latitude], ...]
        var coordinates = request.Coordinates
            .Where(c => c != null && c.Count >= 2)
            .Select(c => new Coordinate(c[0], c[1])) // [0] = longitude, [1] = latitude
            .ToArray();

        if (coordinates.Length < 2)
        {
            return new List<TollDto>();
        }

        var polyline = new LineString(coordinates) { SRID = 4326 };

        // Ищем все точки (Tolls), которые находятся в пределах заданного расстояния от полилинии
        var tolls = await _context.Tolls
            .Where(t => t.Location != null && 
                       t.Location.IsWithinDistance(polyline, request.DistanceMeters))
            .ToListAsync(ct);

        return _mapper.Map<List<TollDto>>(tolls);
    }
}

