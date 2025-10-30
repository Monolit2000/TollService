namespace TollService.Contracts;

public record RoadDto(Guid Id, string Name, string HighwayType, bool IsToll);

public record TollDto(Guid Id, string Name, decimal Price, double Latitude, double Longitude, Guid RoadId);




