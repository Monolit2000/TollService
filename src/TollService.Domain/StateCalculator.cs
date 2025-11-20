using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TollService.Domain
{
    public class StateCalculator
    {

        public Guid Id { get; set; }
        public string Name { get; set; }

        public string StateCode { get; set; }

        //public List<Toll> Tolls { get; set; } = [];
        public List<CalculatePrice> CalculatePrices { get; set; } = []; 

    }
}
