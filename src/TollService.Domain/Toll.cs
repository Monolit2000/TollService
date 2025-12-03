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

    //public List<Toll> TollsGoTo { get; set; }

    public string? Number { get; set; }

    #region calculate 
    public Guid? StateCalculatorId { get; set; }

    #endregion

    #region price 

    public double IPassOvernight { get; set; }

    public double IPass { get; set; }

    public double PayOnlineOvernight { get; set; }

    public double PayOnline { get; set; }

    public int PaPlazaKay { get; set; }
    #endregion
    public List<TollPrice> TollPrices { get; set; } = [];

    public void SetPriceByPaymentType(
        double amount,
        TollPaymentType paymentType,
        AxelType axelType = AxelType._5L,
        TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
        TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
        TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any)
    {
        var price = GetPriceByPaymentType(paymentType, axelType, dayOfWeekFrom, dayOfWeekTo, timeOfDay);
        if (price != null)
        {
            price.Amount = amount;
        }
        else
        {
            var newTollPrice = new TollPrice
            {
                Id = Guid.NewGuid(),
                TollId = this.Id,
                CalculatePriceId = null,
                Amount = amount,
                PaymentType = paymentType,
                AxelType = axelType,
                DayOfWeekFrom = dayOfWeekFrom,
                DayOfWeekTo = dayOfWeekTo,
                TimeOfDay = timeOfDay
            };
            TollPrices.Add(newTollPrice);
        }
    }

    public TollPrice? GetPriceByPaymentType(
        TollPaymentType paymentType,
        AxelType axelType = AxelType._5L,
        TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
        TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
        TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any)
    {
        var existingTollPrice = TollPrices.FirstOrDefault(
            x => x.PaymentType == paymentType &&
            x.AxelType == axelType &&
            x.DayOfWeekFrom == dayOfWeekFrom &&
            x.DayOfWeekTo == dayOfWeekTo &&
            x.TimeOfDay == timeOfDay &&
            !x.IsCalculate());
        return existingTollPrice;
    }

    public double GetAmmountByPaymentType(
        TollPaymentType paymentType,
        AxelType axelType = AxelType._5L,
        TollPriceDayOfWeek dayOfWeekFrom = TollPriceDayOfWeek.Any,
        TollPriceDayOfWeek dayOfWeekTo = TollPriceDayOfWeek.Any,
        TollPriceTimeOfDay timeOfDay = TollPriceTimeOfDay.Any)
    {
        var existingTollPrice = GetPriceByPaymentType(paymentType, axelType, dayOfWeekFrom, dayOfWeekTo, timeOfDay);
        if (existingTollPrice == null)
        {
            if (paymentType == TollPaymentType.EZPass || paymentType == TollPaymentType.IPass)
                return IPass;

            return PayOnline;
        }
        else
        {
            return existingTollPrice.Amount;
        }
    }
}




