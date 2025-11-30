using System;

namespace TollService.Domain;

public class TollPrice
{
    public Guid Id { get; set; }
    public Guid TollId { get; set; }
    public Toll Toll { get; set; } = null!;

    public Guid? CalculatePriceId { get; set; }
    public CalculatePrice? CalculatePrice { get; set; }

    public TollPaymentType PaymentType { get; set; }

    /// <summary>
    /// Тип транспортного средства по количеству осей.
    /// </summary>
    public AxelType AxelType { get; set; } = AxelType.Unknown;

    public TollPriceTimeOfDay TimeOfDay { get; set; } = TollPriceTimeOfDay.Any;
    public TimeOnly TimeFrom { get; set; }
    public TimeOnly TimeTo { get; set; }
    public string? Description { get; set; }
    public double Amount { get; set; }
    //public bool IsCalculate { get; set; }

    // Пустой конструктор для EF Core
    public TollPrice()
    {
    }

    public TollPrice(
        Guid calculatePriceId,
        double amount,
        TollPaymentType paymentType,
        AxelType axelType = AxelType._5L,
        //bool isCalculate = false,
        TimeOnly timeFrom = default,
        TimeOnly timeTo = default,
        string? description = null)
    {
        CalculatePriceId = calculatePriceId;
        Amount = amount;
        PaymentType = paymentType;
        //IsCalculate = isCalculate;
        AxelType = axelType;
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
}

public enum TollPriceTimeOfDay
{
    Any = 0,
    Day = 1,
    Night = 2,
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
}
