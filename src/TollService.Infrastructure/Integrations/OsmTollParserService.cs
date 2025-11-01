using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Text.Json;
using TollService.Domain;

namespace TollService.Infrastructure.Integrations;

public class OsmTollParserService
{
    public List<Toll> ParseTollPointsFromJson(JsonDocument doc, string stateCode, List<Road> existingRoads)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return new List<Toll>();
        }

        var tollsToAdd = new List<Toll>();

        // First pass: collect toll nodes and their parent ways
        var tollNodes = new Dictionary<long, JsonElement>(); // nodeId -> node element
        var wayToNodesMap = new Dictionary<long, List<long>>(); // wayId -> list of nodeIds

        foreach (var el in elements.EnumerateArray())
        {
            if (!el.TryGetProperty("type", out var typeProp)) continue;

            var type = typeProp.GetString();

            if (type == "node")
            {
                // Check if it's a toll point
                var tags = el.TryGetProperty("tags", out var tagsProp) ? tagsProp : default;
                bool isTollPoint = false;

                if (tags.ValueKind == JsonValueKind.Object)
                {
                    if (tags.TryGetProperty("barrier", out var barrierProp) && barrierProp.GetString() == "toll_booth")
                        isTollPoint = true;
                    else if (tags.TryGetProperty("highway", out var highwayProp) && highwayProp.GetString() == "toll_gantry")
                        isTollPoint = true;
                }

                if (isTollPoint && el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                {
                    var nodeId = idProp.GetInt64();
                    tollNodes[nodeId] = el;
                }
            }
            else if (type == "way")
            {
                // Collect way and its nodes
                if (el.TryGetProperty("id", out var wayIdProp) && wayIdProp.ValueKind == JsonValueKind.Number)
                {
                    var wayId = wayIdProp.GetInt64();
                    var nodeIds = new List<long>();

                    if (el.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nodeIdProp in nodesProp.EnumerateArray())
                        {
                            if (nodeIdProp.ValueKind == JsonValueKind.Number)
                            {
                                nodeIds.Add(nodeIdProp.GetInt64());
                            }
                        }
                    }

                    wayToNodesMap[wayId] = nodeIds;
                }
            }
        }

        // Second pass: match toll nodes with their parent ways, then find corresponding Road
        foreach (var (nodeId, nodeEl) in tollNodes)
        {
            // Find which way contains this node
            long? wayId = null;
            foreach (var (wId, nodeIds) in wayToNodesMap)
            {
                if (nodeIds.Contains(nodeId))
                {
                    wayId = wId;
                    break;
                }
            }

            if (!wayId.HasValue) 
                continue;

            double? lat = null;
            double? lon = null;

            if (nodeEl.TryGetProperty("lat", out var latProp) && latProp.ValueKind == JsonValueKind.Number)
                lat = latProp.GetDouble();
            if (nodeEl.TryGetProperty("lon", out var lonProp) && lonProp.ValueKind == JsonValueKind.Number)
                lon = lonProp.GetDouble();

            if (!lat.HasValue || !lon.HasValue) continue;

            var location = new Point(lon.Value, lat.Value) { SRID = 4326 };

            // Find corresponding Road by WayId
            var road = existingRoads.FirstOrDefault(r => r.WayId == wayId.Value);
            if (road == null)
            {

                road = existingRoads.FirstOrDefault(r => r.Geometry != null && r.Geometry.IsWithinDistance(location, 1));

                if(road == null) 
                    continue;
            }

            // Extract toll point data
            string name = string.Empty;
            decimal price = 0;

            if (nodeEl.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            {
                if (tags.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    name = nameProp.GetString() ?? string.Empty;
                if (tags.TryGetProperty("toll", out var tollProp) && tollProp.ValueKind == JsonValueKind.String)
                {
                    if (decimal.TryParse(tollProp.GetString(), out var parsedPrice))
                        price = parsedPrice;
                }
            }


            var toll = new Toll
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(name) ? $"Toll Point {nodeId}" : name,
                Price = price,
                Location = location,
                RoadId = road.Id,
                NodeId = nodeId
            };

            tollsToAdd.Add(toll);
        }

        return tollsToAdd;
    }
}

