using MediatR;
using NetTopologySuite.Geometries;
using TollService.Contracts;
using TollService.Domain;
using TollService.Domain.WeighStations;

namespace TollService.Application.WeighStations.Commands;

public record AddWeighStationsCommand(
    List<WeighStationRequestDto> WeighStations) : IRequest<AddWeighStationsResult>;

public class AddWeighStationsCommandHandler(
    TollService.Application.Common.Interfaces.ITollDbContext _context) 
    : IRequestHandler<AddWeighStationsCommand, AddWeighStationsResult>
{
    public async Task<AddWeighStationsResult> Handle(AddWeighStationsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int addedCount = 0;

        foreach (var weighStationDto in request.WeighStations)
        {
            try
            {
                if (weighStationDto.Location == null || weighStationDto.Location.Count < 2)
                {
                    errors.Add($"Invalid location for '{weighStationDto.Title}': location must contain at least 2 values [longitude, latitude]");
                    continue;
                }

                var longitude = weighStationDto.Location[0];
                var latitude = weighStationDto.Location[1];

                if (longitude < -180 || longitude > 180 || latitude < -90 || latitude > 90)
                {
                    errors.Add($"Invalid coordinates for '{weighStationDto.Title}': longitude must be between -180 and 180, latitude between -90 and 90");
                    continue;
                }

                var location = new NetTopologySuite.Geometries.Point(longitude, latitude) { SRID = 4326 };
                var weighStation = new WeighStation(
                    weighStationDto.Title ?? string.Empty,
                    weighStationDto.Address ?? string.Empty,
                    weighStationDto.Web ?? string.Empty,
                    location);

                _context.WeighStations.Add(weighStation);
                addedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing '{weighStationDto.Title}': {ex.Message}");
            }
        }

        if (addedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new AddWeighStationsResult(addedCount, errors);
    }
}


