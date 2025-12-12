using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.MA;

public record ParseMassachusettsTollsCommand(
    List<MassachusettsTollRequestDto> MassachusettsTollRequestDtos) : IRequest<ParseMassachusettsTollsResult>;

public record ParseMassachusettsTollsResult(
    int ProcessedTolls,
    int UpdatedTolls,
    int CreatedTolls,
    List<string> Errors);

public class ParseMassachusettsTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseMassachusettsTollsCommand, ParseMassachusettsTollsResult>
{
    public async Task<ParseMassachusettsTollsResult> Handle(ParseMassachusettsTollsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processedTolls = 0;
        int updatedTolls = 0;
        int createdTolls = 0;

        try
        {
            foreach (var maToll in request.MassachusettsTollRequestDtos)
            {
                try
                {
                    if (maToll.coordinates == null)
                    {
                        continue;
                    }

                    // Пропускаем записи без координат
                    if (maToll.coordinates.latitude == 0 || maToll.coordinates.longitude == 0)
                    {
                        continue;
                    }

                    // Создаем точку
                    var tollPoint = new Point(maToll.coordinates.longitude, maToll.coordinates.latitude) { SRID = 4326 };

                    // Ищем все существующие Toll в радиусе 100 метров
                    var existingTolls = await FindTollsInRadiusAsync(_context, maToll.coordinates.latitude, maToll.coordinates.longitude, 100, ct);

                    if (/*existingTolls.Count > 0 */ false)
                    {
                        // Обновляем все найденные Toll
                        foreach (var toll in existingTolls)
                        {
                            var changed = false;

                            // Заполняем Number из поля value
                            if (!string.IsNullOrWhiteSpace(maToll.value) && toll.Number != maToll.value)
                            {
                                toll.Number = maToll.value;
                                changed = true;
                            }

                            if (!string.IsNullOrWhiteSpace(maToll.name) && toll.Name != maToll.name)
                            {
                                toll.Name = maToll.name;
                                toll.Key = maToll.name;
                                changed = true;
                            }

                            if (changed)
                            {
                                updatedTolls++;
                            }
                        }
                    }
                    else
                    {
                        // Создаем новый Toll
                        var newToll = new Toll
                        {
                            Id = Guid.NewGuid(),
                            Name = maToll.name ?? string.Empty,
                            Number = maToll.value ?? string.Empty,
                            Location = tollPoint,
                            Key = maToll.name ?? string.Empty,
                            Price = 0,
                            isDynamic = false
                        };

                        _context.Tolls.Add(newToll);
                        createdTolls++;
                    }

                    processedTolls++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Error processing toll {maToll.name ?? "unknown"}: {ex.Message}");
                    processedTolls++;
                }
            }

            if (updatedTolls > 0 || createdTolls > 0)
            {
                await _context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error processing data: {ex.Message}");
        }

        return new ParseMassachusettsTollsResult(processedTolls, updatedTolls, createdTolls, errors);
    }

    private static async Task<List<Toll>> FindTollsInRadiusAsync(
        ITollDbContext context,
        double latitude,
        double longitude,
        double radiusMeters,
        CancellationToken ct)
    {
        const double MetersPerDegree = 111_320.0;
        var point = new Point(longitude, latitude) { SRID = 4326 };
        var radiusDegrees = radiusMeters / MetersPerDegree;

        return await context.Tolls
            .Where(t => t.Location != null && t.Location.IsWithinDistance(point, radiusDegrees))
            .OrderBy(t => t.Location!.Distance(point))
            .ToListAsync(ct);
    }
}

