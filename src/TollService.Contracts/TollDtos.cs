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
    // Радиус поиска (в метрах) для отображения на карте/поиска вокруг толла
    // NB: оставляем имя как в существующем контракте пользователя ("Serch" вместо "Search")
    public double SerchRadiusInMeters { get; set; } = 0;

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
    public string? WebsiteUrl { get; set; }
    public bool IsDynamic { get; set; }
    public double IPassOvernight { get; set; }
    public double IPass { get; set; }
    public double PayOnlineOvernight { get; set; }
    public double PayOnline { get; set; }

    public List<TollWithPriceDto> TollPrices { get; set; } = [];

    // Радиус поиска (в метрах) для отображения на карте/поиска вокруг толла
    // NB: оставляем имя как в существующем контракте пользователя ("Serch" вместо "Search")
    public double SerchRadiusInMeters { get; set; } = 0;

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
        string? websiteUrl = null,
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
        WebsiteUrl = websiteUrl;
        IsDynamic = isDynamic;
        IPassOvernight = iPassOvernight;
        IPass = iPass;
        PayOnlineOvernight = payOnlineOvernight;
        PayOnline = payOnline;
    }
}

public class TollWithPriceDto
{
    public Guid Id { get; set; }
    public Guid? TollId { get; set; }
    public Guid? CalculatePriceId { get; set; }
    public TollPaymentType PaymentType { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new PaymentMethodDto();

    public AxelType AxelType { get; set; } = AxelType.Unknown;

    public TollPriceTimeOfDay TimeOfDay { get; set; } = TollPriceTimeOfDay.Any;
    public TollPriceDayOfWeek DayOfWeekFrom { get; set; } = TollPriceDayOfWeek.Any;
    public TollPriceDayOfWeek DayOfWeekTo { get; set; } = TollPriceDayOfWeek.Any;
    public TimeOnly TimeFrom { get; set; }
    public TimeOnly TimeTo { get; set; }
    public string? Description { get; set; }
    public double Amount { get; set; }
}

public class PaymentMethodDto
{
    public bool Tag { get; set; }
    public bool NoPlate { get; set; }
    public bool Cash { get; set; }
    public bool NoCard { get; set; }
    public bool App { get; set; }
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

public record CreateTollPriceDto(
    Guid? TollId,
    Guid? CalculatePriceId,
    double Amount,
    TollPaymentType PaymentType,
    PaymentMethodDto? PaymentMethod = null,
    AxelType AxelType = AxelType._5L,
    TollPriceDayOfWeek DayOfWeekFrom = TollPriceDayOfWeek.Any,
    TollPriceDayOfWeek DayOfWeekTo = TollPriceDayOfWeek.Any,
    TollPriceTimeOfDay TimeOfDay = TollPriceTimeOfDay.Any,
    TimeOnly TimeFrom = default,
    TimeOnly TimeTo = default,
    string? Description = null);

public record UpdateTollPriceDto(
    double? Amount = null,
    TollPaymentType? PaymentType = null,
    PaymentMethodDto? PaymentMethod = null,
    AxelType? AxelType = null,
    TollPriceTimeOfDay? TimeOfDay = null,
    TollPriceDayOfWeek? DayOfWeekFrom = null,
    TollPriceDayOfWeek? DayOfWeekTo = null,
    TimeOnly? TimeFrom = null,
    TimeOnly? TimeTo = null,
    string? Description = null);

public record MassachusettsTollRequestDto(
    string type,
    string value,
    string? new_number,
    string? old_number,
    string? name,
    MassachusettsCoordinatesDto? coordinates);

public record MassachusettsCoordinatesDto(
    double latitude,
    double longitude,
    string? note);