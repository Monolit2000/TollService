using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TollService.Domain
{
    public class RoadGeometry
    {
        public List<Point> Points { get; set; } = new();
    }
}
