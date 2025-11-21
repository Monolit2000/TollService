using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Reflection;
using TollService.Application.Common.Interfaces;
using TollService.Application.Roads.Commands.CalculateRoutePrice;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Tolls.Queries;

public record GetTollsAlongPolylineSectionsQuery(
    List<PolylineSectionRequestDto> Sections) : IRequest<List<TollWithRouteSectionDto>>;

public class GetTollsAlongPolylineSectionsQueryHandler(
    IMapper _mapper,
    ISender sender,
    ITollDbContext _context) : IRequestHandler<GetTollsAlongPolylineSectionsQuery, List<TollWithRouteSectionDto>>
{
    private const double MetersPerDegree = 111_320.0;

    public async Task<List<TollWithRouteSectionDto>> Handle(GetTollsAlongPolylineSectionsQuery request, CancellationToken ct)
    {
        var allTolls = new List<TollWithRouteSectionDto>();

        foreach (var section in request.Sections)
        {
            if (section.Coordinates == null || section.Coordinates.Count < 2)
            {
                continue;
            }

            var coordinates = section.Coordinates
                .Where(c => c != null && c.Count >= 2)
                .Select(c => new Coordinate(c[1], c[0])) // [0] = latitude, [1] = longitude
                .ToArray();

            if (coordinates.Length < 2)
            {
                continue;
            }

            var polyline = new LineString(coordinates) { SRID = 4326 };

            var meters = 20.0;
            var degrees = meters / MetersPerDegree; // ~0.0000449 градусов

            var tolls = await _context.Tolls
                .Where(t => t.Location != null &&
                           t.Location.IsWithinDistance(polyline, degrees))
                .ToListAsync(ct);

            // Создаем словарь для быстрого поиска toll по Id
            var tollsDict = tolls.ToDictionary(t => t.Id);

            // Маппим в TollDto с добавлением RouteSection и Distance
            var tollDtos = _mapper.Map<List<TollDto>>(tolls);
            var tollsWithSection = tollDtos.Select(t =>
            {
                var toll = tollsDict.TryGetValue(t.Id, out var foundToll) ? foundToll : null;
                var distance = toll?.Location != null 
                    ? CalculateDistanceAlongPolyline(polyline, toll.Location) 
                    : 0.0;

                return new TollWithRouteSectionDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    NodeId = t.NodeId,
                    Price = t.Price,
                    Latitude = t.Latitude,
                    Longitude = t.Longitude,
                    RoadId = t.RoadId,
                    Key = t.Key,
                    Comment = t.Comment,
                    IsDynamic = t.IsDynamic,
                    RouteSection = section.RouteSection,
                    IPassOvernight = t.IPassOvernight,
                    IPass = t.IPass,
                    PayOnlineOvernight = t.PayOnlineOvernight,
                    PayOnline = t.PayOnline,
                    Distance = distance
                };
            }).ToList();

            allTolls.AddRange(tollsWithSection);
        }

        var tollWithPrice = await sender.Send(new CalculateRoutePriceCommand(allTolls));

        List<TollWithRouteSectionDto> tollsWitPrice = new List<TollWithRouteSectionDto>();

        foreach (var toll in allTolls)
        {
            var pricedToll = tollWithPrice.FirstOrDefault(t => t.Toll.Id == toll.Id);
            if (pricedToll == null)
                continue;

            toll.IPass = pricedToll.IPass;
            toll.PayOnline = pricedToll.PayOnline;
            toll.IPassOvernight = pricedToll.PayOnline;
            toll.PayOnlineOvernight = pricedToll.PayOnlineOvernight;
            tollsWitPrice.Add(toll);
        }

        return tollsWitPrice;
    }

    /// <summary>
    /// Вычисляет расстояние вдоль полилинии от начала до проекции точки на линию (в метрах)
    /// </summary>
    private double CalculateDistanceAlongPolyline(LineString polyline, Point point)
    {
        if (polyline == null || point == null || polyline.NumPoints < 2)
            return 0.0;

        // Находим ближайшую точку на линии к заданной точке
        var closestPoint = FindClosestPointOnLine(polyline, point);
        
        // Вычисляем расстояние вдоль линии от начала до ближайшей точки
        return CalculateDistanceAlongLine(polyline, closestPoint);
    }

    /// <summary>
    /// Находит ближайшую точку на линии к заданной точке
    /// </summary>
    private Coordinate FindClosestPointOnLine(LineString line, Point point)
    {
        if (line.NumPoints == 0)
            return point.Coordinate;

        double minDistance = double.MaxValue;
        Coordinate closestPoint = line.StartPoint.Coordinate;

        // Проверяем все сегменты линии
        for (int i = 0; i < line.NumPoints - 1; i++)
        {
            var p1 = line.GetPointN(i).Coordinate;
            var p2 = line.GetPointN(i + 1).Coordinate;
            
            // Находим проекцию точки на сегмент
            var projected = ProjectPointOnSegment(point.Coordinate, p1, p2);
            
            // Вычисляем расстояние от точки до проекции
            var distance = CalculateHaversineDistance(point.Coordinate, projected);
            
            if (distance < minDistance)
            {
                minDistance = distance;
                closestPoint = projected;
            }
        }

        return closestPoint;
    }

    /// <summary>
    /// Проецирует точку на сегмент линии
    /// </summary>
    private Coordinate ProjectPointOnSegment(Coordinate point, Coordinate segmentStart, Coordinate segmentEnd)
    {
        var dx = segmentEnd.X - segmentStart.X;
        var dy = segmentEnd.Y - segmentStart.Y;
        
        if (dx == 0 && dy == 0)
            return segmentStart;

        var t = ((point.X - segmentStart.X) * dx + (point.Y - segmentStart.Y) * dy) / (dx * dx + dy * dy);
        
        // Ограничиваем проекцию границами сегмента
        t = Math.Max(0, Math.Min(1, t));
        
        return new Coordinate(
            segmentStart.X + t * dx,
            segmentStart.Y + t * dy
        );
    }

    /// <summary>
    /// Вычисляет расстояние вдоль линии от начала до заданной точки на линии
    /// </summary>
    private double CalculateDistanceAlongLine(LineString line, Coordinate targetPoint)
    {
        double totalDistance = 0.0;
        double minDistanceToTarget = double.MaxValue;
        int closestSegmentIndex = 0;
        Coordinate closestPointOnSegment = line.StartPoint.Coordinate;

        // Находим сегмент, на котором находится целевая точка
        for (int i = 0; i < line.NumPoints - 1; i++)
        {
            var p1 = line.GetPointN(i).Coordinate;
            var p2 = line.GetPointN(i + 1).Coordinate;
            
            var projected = ProjectPointOnSegment(targetPoint, p1, p2);
            var distanceToTarget = CalculateHaversineDistance(targetPoint, projected);
            
            if (distanceToTarget < minDistanceToTarget)
            {
                minDistanceToTarget = distanceToTarget;
                closestSegmentIndex = i;
                closestPointOnSegment = projected;
            }
        }

        // Суммируем расстояния до найденного сегмента
        for (int i = 0; i < closestSegmentIndex; i++)
        {
            var p1 = line.GetPointN(i).Coordinate;
            var p2 = line.GetPointN(i + 1).Coordinate;
            totalDistance += CalculateHaversineDistance(p1, p2);
        }

        // Добавляем расстояние по последнему сегменту до проекции
        var segmentStart = line.GetPointN(closestSegmentIndex).Coordinate;
        totalDistance += CalculateHaversineDistance(segmentStart, closestPointOnSegment);

        return totalDistance;
    }

    /// <summary>
    /// Вычисляет расстояние между двумя точками по формуле гаверсинуса (в метрах)
    /// </summary>
    private double CalculateHaversineDistance(Coordinate p1, Coordinate p2)
    {
        const double earthRadiusMeters = 6371000; // Радиус Земли в метрах
        
        var lat1 = p1.Y * Math.PI / 180.0;
        var lat2 = p2.Y * Math.PI / 180.0;
        var deltaLat = (p2.Y - p1.Y) * Math.PI / 180.0;
        var deltaLon = (p2.X - p1.X) * Math.PI / 180.0;

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        
        return earthRadiusMeters * c;
    }
}

