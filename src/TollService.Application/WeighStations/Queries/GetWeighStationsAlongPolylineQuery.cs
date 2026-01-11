using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;

namespace TollService.Application.WeighStations.Queries;

public record GetWeighStationsAlongPolylineQuery(
    List<WeighStationSectionRequestDto> Sections) : IRequest<List<WeighStationsBySectionDto>>;

public class GetWeighStationsAlongPolylineQueryHandler(
    ITollDbContext _context,
    IConfiguration _configuration) 
    : IRequestHandler<GetWeighStationsAlongPolylineQuery, List<WeighStationsBySectionDto>>
{
    private const double MetersPerDegree = 111_320.0;

    public async Task<List<WeighStationsBySectionDto>> Handle(GetWeighStationsAlongPolylineQuery request, CancellationToken ct)
    {
        var result = new List<WeighStationsBySectionDto>();

        if (request.Sections == null || request.Sections.Count == 0)
        {
            return result;
        }

        var radiusMeters = _configuration.GetValue<double>("WeighStationSearch:RadiusMeters", 20.0);
        var radiusDegrees = radiusMeters / MetersPerDegree;

        foreach (var section in request.Sections)
        {
            if (section.Coordinates == null || section.Coordinates.Count < 2)
            {
                result.Add(new WeighStationsBySectionDto(new List<WeighStationDto>(), section.SectionId));
                continue;
            }

            var coordinates = section.Coordinates
                .Where(c => c != null && c.Count >= 2)
                .Select(c => new Coordinate(c[0], c[1])) // [0] = longitude, [1] = latitude
                .ToArray();

            if (coordinates.Length < 2)
            {
                result.Add(new WeighStationsBySectionDto(new List<WeighStationDto>(), section.SectionId));
                continue;
            }

            var polyline = new LineString(coordinates) { SRID = 4326 };

            // Ищем все WeighStations, которые находятся в пределах заданного расстояния от полилинии
            var weighStations = await _context.WeighStations
                .Where(ws => ws.Location != null && 
                           ws.Location.IsWithinDistance(polyline, radiusDegrees))
                .ToListAsync(ct);

            // Маппим в DTO
            var weighStationDtos = weighStations
                .Where(ws => ws.Location != null)
                .Select(ws => new WeighStationDto(
                    ws.Id,
                    ws.Title,
                    ws.Address,
                    ws.Web,
                    ws.Location!.Y, // Latitude
                    ws.Location.X  // Longitude
                ))
                .ToList();

            result.Add(new WeighStationsBySectionDto(weighStationDtos, section.SectionId));
        }

        return result;
    }
}

