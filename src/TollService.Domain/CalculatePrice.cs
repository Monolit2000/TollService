using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public double Online { get; set; }
        public double IPass { get; set; }
        public double Cash { get; set; }

    }
}
