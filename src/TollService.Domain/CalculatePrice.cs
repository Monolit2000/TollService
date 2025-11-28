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


        /// <summary>
        /// Дополнительные цены/тарифы для этого расчёта.
        /// Пока это просто навигационное свойство без явной конфигурации.
        /// </summary>
        public List<TollPrice> TollPrices { get; set; } = [];

        public void AddTollPrice(TollPrice tollPrice)
        {
            if (tollPrice != null)
                TollPrices.Add(tollPrice);
        }

        public void AddTollPrices(List<TollPrice> tollPrices)
        {
            if (tollPrices.Any())
                TollPrices.AddRange(tollPrices);
        }
    }
}
