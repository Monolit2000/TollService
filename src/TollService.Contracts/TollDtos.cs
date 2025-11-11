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

public record PolylineRequestDto(List<List<double>> Coordinates, double? DistanceMeters = 1);

public record PolylineSectionRequestDto(
    List<List<double>> Coordinates, 
    double? DistanceMeters = 1, 
    string? RouteSection = null);

public record TollWithRouteSectionDto(
    Guid Id, 
    string Name, 
    long NodeId, 
    decimal Price, 
    double Latitude, 
    double Longitude, 
    Guid RoadId, 
    string? Key, 
    string? Comment, 
    bool IsDynamic, 
    string? RouteSection);

public record TollDto(Guid Id, string Name, long NodeId, decimal Price, double Latitude, double Longitude, Guid RoadId, string? Key, string? Comment, bool IsDynamic = false);