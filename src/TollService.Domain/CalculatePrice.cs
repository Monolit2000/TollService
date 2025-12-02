using System;
using System.Collections.Generic;

namespace TollService.Domain
{
    public class CalculatePrice
    {
        public Guid Id { get; set; }

        public Guid StateCalculatorId { get; set; }
        public StateCalculator StateCalculator { get; set; }

        public Guid FromId { get; set; }
        public Toll From { get; set; }

        public Guid ToId { get; set; }
        public Toll To { get; set; }

        public double Online { get; set; } = 0;
        public double IPass { get; set; } = 0;
        public double Cash { get; set; } = 0;

        public List<TollPrice> TollPrices { get; set; } = [];

        public TollPrice SetPriceByPaymentType(
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
                return price;
            }
            else
            {
                // Используем FromId как TollId для TollPrice
                var newTollPrice = new TollPrice(this.Id, amount, paymentType, axelType, dayOfWeekFrom, dayOfWeekTo, timeOfDay);
                TollPrices.Add(newTollPrice);
                return newTollPrice;
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
                x.TimeOfDay == timeOfDay);
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

                return Cash;
            }
            else
            {
                return existingTollPrice.Amount;
            }
        }
    }
}
