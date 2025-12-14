namespace TollService.Domain;

public class TollPrice
{
    public Guid Id { get; set; }
    public Guid? TollId { get; set; }
    public Toll? Toll { get; set; } = null!;

    public Guid? CalculatePriceId { get; set; }
    public CalculatePrice? CalculatePrice { get; set; }

    public TollPaymentType PaymentType { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Default();

    /// <summary>
    /// Тип транспортного средства по количеству осей.
    /// </summary>
    public AxelType AxelType { get; set; } = AxelType.Unknown;

    public TollPriceTimeOfDay TimeOfDay { get; set; } = TollPriceTimeOfDay.Any;
    public TollPriceDayOfWeek DayOfWeekFrom { get; set; } = TollPriceDayOfWeek.Any;
    public TollPriceDayOfWeek DayOfWeekTo { get; set; } = TollPriceDayOfWeek.Any;
    public TimeOnly TimeFrom { get; set; }
    public TimeOnly TimeTo { get; set; }
    public string? Description { get; set; }
    public double Amount { get; set; }
    //public bool IsCalculate { get; set; }

    // Пустой конструктор для EF Core
    public TollPrice()
    {
        PaymentMethod = PaymentMethod.Default();
    }

    public TollPrice(
    Guid? tollId,
    Guid? calculatePriceId,
    double amount,
    TollPaymentType paymentType,
    PaymentMethod? paymentMethod = null,
    AxelType axelType = AxelType._5L,
    TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
    TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
    TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any,
    //bool isCalculate = false,
    TimeOnly timeFrom = default,
    TimeOnly timeTo = default,
    string? description = null)
    {
        if (tollId == null && calculatePriceId == null)
        {
            throw new ArgumentException("Either tollId or calculatePriceId must be provided.");
        }

        Id = Guid.NewGuid();
        TollId = tollId;
        CalculatePriceId = calculatePriceId;
        Amount = amount;
        PaymentType = paymentType;
        PaymentMethod = paymentMethod ?? PaymentMethod.Default();
        //IsCalculate = isCalculate;
        AxelType = axelType;
        DayOfWeekFrom = dayOfWeekFrom;
        DayOfWeekTo = dayOfWeekTo;
        TimeOfDay = timeOfDay;
        TimeFrom = timeFrom;
        TimeTo = timeTo;
        Description = description;
    }

    public TollPrice(
        Guid calculatePriceId,
        double amount,
        TollPaymentType paymentType,
        PaymentMethod? paymentMethod = null,
        AxelType axelType = AxelType._5L,
        TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
        TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
        TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any,
        //bool isCalculate = false,
        TimeOnly timeFrom = default,
        TimeOnly timeTo = default,
        string? description = null)
    {
        Id = Guid.NewGuid();
        CalculatePriceId = calculatePriceId;
        Amount = amount;
        PaymentType = paymentType;
        PaymentMethod = paymentMethod ?? PaymentMethod.Default();
        //IsCalculate = isCalculate;
        AxelType = axelType;
        DayOfWeekFrom = dayOfWeekFrom;
        DayOfWeekTo = dayOfWeekTo;
        TimeOfDay = timeOfDay;
        TimeFrom = timeFrom;
        TimeTo = timeTo;
        Description = description;
    }
}

public static class TollPriceExtensions
{
    public static bool IsCalculate(this TollPrice tollPrice) => tollPrice.CalculatePriceId.HasValue;
}

public enum TollPaymentType
{
    Unknown = 0,
    IPass = 1,
    PayOnline = 2,
    Cash = 3,
    EZPass = 4,
    OutOfStateEZPass = 5,
    VideoTolls = 6,
    SunPass = 7,
    AccountToll = 8,
    NonAccountToll = 9,
}

public enum TollPriceTimeOfDay
{
    Any = 0,
    Day = 1,
    Night = 2,
}

public enum TollPriceDayOfWeek
{
    Any = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Sunday = 7,
}

public enum AxelType
{
    Unknown = 0,

    /// <summary>
    /// Класс 1L — легковой автомобиль / мотоцикл (2 оси).
    /// </summary>
    _1L = 1,

    /// <summary>
    /// Класс 2L — грузовик / связка с 3 осями.
    /// </summary>
    _2L = 2,

    /// <summary>
    /// Класс 3L — ТС с 4 осями.
    /// </summary>
    _3L = 3,

    /// <summary>
    /// Класс 4L — ТС с 5 и более осями.
    /// </summary>
    _4L = 4,

    /// <summary>
    /// Класс 5L.
    /// </summary>
    _5L = 5,

    /// <summary>
    /// Класс 6L.
    /// </summary>
    _6L = 6,

    _7L = 7,

    _8L = 8,

    _9L = 9
}

/// <summary>
/// ValueObject для методов оплаты с булевыми полями.
/// </summary>
public class PaymentMethod
{
    public bool Tag { get; set; }
    public bool NoPlate { get; set; }
    public bool Cash { get; set; }
    public bool NoCard { get; set; }
    public bool App { get; set; }

    // Пустой конструктор для EF Core
    public PaymentMethod()
    {
    }

    public PaymentMethod(bool tag = false, bool noPlate = false, bool cash = false, bool noCard = false, bool app = false)
    {
        Tag = tag;
        NoPlate = noPlate;
        Cash = cash;
        NoCard = noCard;
        App = app;
    }

    /// <summary>
    /// Создает PaymentMethod с только Tag = true (значение по умолчанию).
    /// </summary>
    public static PaymentMethod Default() => new PaymentMethod(tag: true);

    /// <summary>
    /// Проверяет, выбран ли хотя бы один метод оплаты.
    /// </summary>
    public bool HasAnyMethod() => Tag || NoPlate || Cash || NoCard || App;

    public override bool Equals(object? obj)
    {
        if (obj is not PaymentMethod other)
            return false;

        return Tag == other.Tag &&
               NoPlate == other.NoPlate &&
               Cash == other.Cash &&
               NoCard == other.NoCard &&
               App == other.App;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Tag, NoPlate, Cash, NoCard, App);
    }
}