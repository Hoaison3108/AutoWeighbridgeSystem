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

        // Trả về số cân VÀ cờ ổn định của phần cứng
        (decimal Weight, bool IsHardwareStable)? ParseWeight(string rawData);
    }
}
