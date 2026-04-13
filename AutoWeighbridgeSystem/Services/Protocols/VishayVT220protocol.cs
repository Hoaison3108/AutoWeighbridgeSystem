using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services.Protocols
{
    public class VishayVT220protocol : IScaleProtocol
    {
        public string ProtocolName => "VishayVT220";

        public decimal? ParseWeight(string rawData)
        {
            var match = Regex.Match(rawData, @"[+-]?\d+");
            if (match.Success && decimal.TryParse(match.Value, out decimal weight))
                return weight;
            return null;
        }

    }
}
