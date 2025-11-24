using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Text.Json;
using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.KS;

public record ParseKansasTollsCommand(
    List<KansasTollRequestDto> KansasTollRequestDtos) : IRequest<ParseKansasTollsResult>;

public record ParseKansasTollsResult(
    int ProcessedTolls,
    int UpdatedTolls,
    int CreatedTolls,
    List<string> Errors);

public class ParseKansasTollsCommandHandler(
    ITollDbContext _context) : IRequestHandler<ParseKansasTollsCommand, ParseKansasTollsResult>
{
    public async Task<ParseKansasTollsResult> Handle(ParseKansasTollsCommand request, CancellationToken ct)
    {
        var errors = new List<string>();
        int processedTolls = 0;
        int updatedTolls = 0;
        int createdTolls = 0;

        try
        {
            foreach (var item in request.KansasTollRequestDtos)
            {
                try
                {
                    if (item.position == null)
                    {
                        continue;
                    }

                    string? valueStr = null;
                    if (item.value != null)
                    {
                        if (item.value is JsonElement element)
                        {
                             valueStr = element.ToString();
                        }
                        else
                        {
                             valueStr = item.value.ToString();
                        }
                    }

                    // Find tolls in 100m radius
                    var existingTolls = await FindTollsInRadiusAsync(_context, item.position.lat, item.position.lng, 100, ct);

                    if (existingTolls.Count > 0)
                    {
                        // Update existing tolls
                        foreach (var toll in existingTolls)
                        {
                            var changed = false;

                            if (!string.IsNullOrWhiteSpace(valueStr) && toll.Number != valueStr)
                            {
                                toll.Number = valueStr;
                                changed = true;
                            }

                            if (!string.IsNullOrWhiteSpace(item.title) && toll.Name != item.title)
                            {
                                toll.Name = item.title;
                                toll.Key = item.title;
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
                        // Create new toll if none found
                        var tollPoint = new Point(item.position.lng, item.position.lat) { SRID = 4326 };
                        var newToll = new Toll
                        {
                            Id = Guid.NewGuid(),
                            Name = item.title ?? string.Empty,
                            Number = valueStr ?? string.Empty,
                            Location = tollPoint,
                            Key = item.title ?? string.Empty,
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
                    errors.Add($"Error processing toll {item.title ?? "unknown"}: {ex.Message}");
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

        return new ParseKansasTollsResult(processedTolls, updatedTolls, createdTolls, errors);
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

