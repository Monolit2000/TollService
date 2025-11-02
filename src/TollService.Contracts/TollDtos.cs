namespace TollService.Contracts;

public record RoadDto(Guid Id, string Name, string HighwayType, bool IsToll);

public record RoadWithGeometryDto(
    Guid Id, 
    string Name, 
    string Ref,
    string HighwayType, 
    bool IsToll, 
    List<PointDto> Coordinates);

public record PointDto(double Latitude, double Longitude);

public record TollDto(Guid Id, string Name, decimal Price, double Latitude, double Longitude, Guid RoadId);




