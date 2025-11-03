using NetTopologySuite.Geometries;

namespace TollService.Domain;

public class Toll
{
    public Guid Id { get; set; }
    public string? Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public Point? Location { get; set; }
    public Guid? RoadId { get; set; }
    public Road? Road { get; set; }
    public long? NodeId { get; set; }
    public string? Key { get; set; }
    public string? Comment { get; set; }    
}




