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
    public bool isDynamic { get; set; } = false;

    public int Number { get; set; }

    #region price 

    public double IPassOvernight { get; set; }

    public double IPass { get; set; }

    public double PayOnlineOvernight { get; set; }

    public double PayOnline { get; set; }

    #endregion
}




