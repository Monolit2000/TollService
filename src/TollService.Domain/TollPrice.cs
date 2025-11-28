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
    public TollPriceTimeOfDay TimeOfDay { get; set; } = TollPriceTimeOfDay.Any;
    public TimeOnly TimeFrom { get; set; }
    public TimeOnly TimeTo { get; set; }
    public string? Description { get; set; }
    public double Amount { get; set; }
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



