using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.Tolls.Queries;

public record GetTollsAlongPolylineSectionsQuery(
    List<PolylineSectionRequestDto> Sections) : IRequest<List<TollWithRouteSectionDto>>;

public class GetTollsAlongPolylineSectionsQueryHandler(
    IMapper _mapper,
    ITollDbContext _context) : IRequestHandler<GetTollsAlongPolylineSectionsQuery, List<TollWithRouteSectionDto>>
{
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
                .Select(c => new Coordinate(c[0], c[1])) // [0] = latitude, [1] = longitude
                .ToArray();

            if (coordinates.Length < 2)
            {
                continue;
            }

            var polyline = new LineString(coordinates) { SRID = 4326 };
            var distanceMeters = section.DistanceMeters ?? 1;

            var tolls = await _context.Tolls
                .Where(t => t.Location != null && 
                           t.Location.IsWithinDistance(polyline, distanceMeters))
                .ToListAsync(ct);

            // Маппим в TollWithRouteSectionDto с добавлением RouteSection
            var tollDtos = _mapper.Map<List<TollDto>>(tolls);
            var tollsWithSection = tollDtos.Select(t => new TollWithRouteSectionDto(
                t.Id,
                t.Name,
                t.NodeId,
                t.Price,
                t.Latitude,
                t.Longitude,
                t.RoadId,
                t.Key,
                t.Comment,
                t.IsDynamic,
                section.RouteSection
            )).ToList();

            allTolls.AddRange(tollsWithSection);
        }

        return allTolls;
    }
}

