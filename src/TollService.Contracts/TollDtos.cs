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

public class TollWithRouteSectionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long NodeId { get; set; }
    public decimal Price { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Guid RoadId { get; set; }
    public string? Key { get; set; }
    public string? Comment { get; set; }
    public bool IsDynamic { get; set; }
    public string? RouteSection { get; set; }
    public double IPassOvernight { get; set; }
    public double IPass { get; set; }
    public double PayOnlineOvernight { get; set; }
    public double PayOnline { get; set; }
    public double Distance { get; set; } = 0;
    public double OrderId { get; set; } = 0;
}

public record TollDto(
    Guid Id, 
    string Name, 
    long NodeId, 
    decimal Price, 
    double Latitude, 
    double Longitude, 
    Guid RoadId, 
    string? Key, 
    string? Comment, 
    bool IsDynamic = false,
    double IPassOvernight = 0,
    double IPass = 0,
    double PayOnlineOvernight = 0,
    double PayOnline = 0);

public record IndianaTollPriceRequestDto(
    string entry,
    string exit,
    string cash_rate,
    string avi_rate);

public record IndianaTollRequestDto(
    string name,
    string toll,
    string ramps,
    int mile,
    double lat,
    double lng,
    string direction,
    string barrier_number,
    string system);

public record OhioTollRequestDto(
    string name,
    double lat,
    double lng);

public record KansasTollRequestDto(
    object? value,
    KansasPositionDto? position,
    string? title);

public record KansasPositionDto(
    double lat,
    double lng);

public record KansasVehicleClassDto(
    int Axles,
    string Name);

public record KansasPlazaDto(
    int Value,
    string Name);

public record KansasCtsRateDto(
    int ZoneCode,
    int Class,
    decimal TransponderRate);

public record KansasSampleResultDto(
    decimal TBR,
    decimal IBR);

public record KansasCalculatorRequestDto(
    List<KansasVehicleClassDto> VehicleClasses,
    List<KansasPlazaDto> Plazas,
    List<KansasCtsRateDto> CtsRates,
    KansasSampleResultDto? SampleResult);