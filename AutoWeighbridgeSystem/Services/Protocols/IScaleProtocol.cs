using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services.Protocols
{
    public interface IScaleProtocol
    {
        // Tên định danh của chuẩn (vd: Yaohua, Kingbird, Cas)
        string ProtocolName { get; }

        // Hàm bóc tách số từ chuỗi thô
        decimal? ParseWeight(string rawData);
    }
}
