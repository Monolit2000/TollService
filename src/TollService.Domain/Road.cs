using NetTopologySuite.Geometries;

namespace TollService.Domain;

public class Road
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HighwayType { get; set; } = string.Empty;
    public bool IsToll { get; set; }
    public LineString? Geometry { get; set; }
    public List<Toll> Tolls { get; set; } = new();
}




