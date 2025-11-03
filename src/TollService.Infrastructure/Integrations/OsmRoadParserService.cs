using System.Text.Json;
using NetTopologySuite.Geometries;
using TollService.Domain;

namespace TollService.Infrastructure.Integrations;

public class OsmRoadParserService
{
    public List<Road> ParseTollRoadsFromJson(JsonDocument doc, string stateCode)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return new List<Road>();
        }

        var roadsToAdd = new List<Road>();

        foreach (var el in elements.EnumerateArray())
        {
            if (!el.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "way") 
                continue;

            // Extract WayId from OSM element id
            long? wayId = null;
            if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                wayId = idProp.GetInt64();
            }

            // tags
            string name = string.Empty;
            string highwayType = string.Empty;
            string refCode = string.Empty;
            if (el.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            {
                if (tags.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    name = nameProp.GetString() ?? string.Empty;
                if (tags.TryGetProperty("highway", out var hwProp) && hwProp.ValueKind == JsonValueKind.String)
                    highwayType = hwProp.GetString() ?? string.Empty;
                if (tags.TryGetProperty("ref", out var refProp) && refProp.ValueKind == JsonValueKind.String)
                    refCode = refProp.GetString() ?? string.Empty;
            }

            // geometry -> LineString
            if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array)
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
                State = stateCode,
                Ref = string.IsNullOrWhiteSpace(refCode) ? null : refCode,
                WayId = wayId,
                Geometry = line
            };

            roadsToAdd.Add(road);
        }

        return roadsToAdd;
    }
}

