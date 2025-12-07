using TollService.Domain;

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

    public List<TollPrice> TollPrices { get; set; } = [];
}

public class TollDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public long NodeId { get; set; }
    public decimal Price { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Guid RoadId { get; set; }
    public string? Key { get; set; }
    public string? Comment { get; set; }
    public bool IsDynamic { get; set; }
    public double IPassOvernight { get; set; }
    public double IPass { get; set; }
    public double PayOnlineOvernight { get; set; }
    public double PayOnline { get; set; }

    public List<TollPriceDto> TollPrices { get; set; } = [];

    public TollDto()
    {
            
    }

    public TollDto(
        Guid id,
        string name,
        long nodeId,
        decimal price,
        double latitude,
        double longitude,
        Guid roadId,
        string? key,
        string? comment,
        bool isDynamic = false,
        double iPassOvernight = 0,
        double iPass = 0,
        double payOnlineOvernight = 0,
        double payOnline = 0)
    {
        Id = id;
        Name = name;
        NodeId = nodeId;
        Price = price;
        Latitude = latitude;
        Longitude = longitude;
        RoadId = roadId;
        Key = key;
        Comment = comment;
        IsDynamic = isDynamic;
        IPassOvernight = iPassOvernight;
        IPass = iPass;
        PayOnlineOvernight = payOnlineOvernight;
        PayOnline = payOnline;
    }
}

public class TollPriceDto
{
    public Guid Id { get; set; }
    public Guid? TollId { get; set; }
    public Guid? CalculatePriceId { get; set; }
    public TollPaymentType PaymentType { get; set; }

    public AxelType AxelType { get; set; } = AxelType.Unknown;

    public TollPriceTimeOfDay TimeOfDay { get; set; } = TollPriceTimeOfDay.Any;
    public TollPriceDayOfWeek DayOfWeekFrom { get; set; } = TollPriceDayOfWeek.Any;
    public TollPriceDayOfWeek DayOfWeekTo { get; set; } = TollPriceDayOfWeek.Any;
    public TimeOnly TimeFrom { get; set; }
    public TimeOnly TimeTo { get; set; }
    public string? Description { get; set; }
    public double Amount { get; set; }
}

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