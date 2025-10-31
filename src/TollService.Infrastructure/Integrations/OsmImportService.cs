using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Domain;
using TollService.Infrastructure.Persistence;

namespace TollService.Infrastructure.Integrations;

public class OsmImportService
{
    private readonly OsmClient _osmClient;
    private readonly TollDbContext _context;

    public OsmImportService(OsmClient osmClient, TollDbContext context)
    {
        _osmClient = osmClient;
        _context = context;
    }

    public async Task ImportTexasAsync(CancellationToken ct = default)
    {
        // Placeholder: fetch data to validate pipeline; persisting can be added later
        await _osmClient.GetTollDataForTexasAsync(ct);
    }

    public async Task ImportLosAngelesTollRoadsAsync(CancellationToken ct = default)
    {
        // BBOX: 32.5,-124.5,42.0,-114.0
        using var doc = await _osmClient.GetTollRoadWaysAsync(32.5, -124.5, 42.0, -114.0, ct);
        var root = doc.RootElement;
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        var roadsToAdd = new List<Road>();

        foreach (var el in elements.EnumerateArray())
        {
            if (!el.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "way") continue;

            // tags
            string name = string.Empty;
            string highwayType = string.Empty;
            if (el.TryGetProperty("tags", out var tags) && tags.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (tags.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    name = nameProp.GetString() ?? string.Empty;
                if (tags.TryGetProperty("highway", out var hwProp) && hwProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    highwayType = hwProp.GetString() ?? string.Empty;
            }

            // geometry -> LineString
            if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;

            var coords = new List<Coordinate>();
            foreach (var pt in geom.EnumerateArray())
            {
                if (!pt.TryGetProperty("lat", out var latProp) || !pt.TryGetProperty("lon", out var lonProp)) continue;
                double lat = latProp.GetDouble();
                double lon = lonProp.GetDouble();
                coords.Add(new Coordinate(lon, lat));
            }

            if (coords.Count < 2) continue;

            var line = new LineString(coords.ToArray()) { SRID = 4326 };

            var road = new Road
            {
                Id = Guid.NewGuid(),
                Name = name,
                HighwayType = highwayType,
                IsToll = true,
                State = "CA",
                Geometry = line
            };

            roadsToAdd.Add(road);
        }

        if (roadsToAdd.Count == 0) return;

        await _context.Roads.AddRangeAsync(roadsToAdd, ct);
        await _context.SaveChangesAsync(ct);
    }
}


