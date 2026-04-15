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

        public (decimal Weight, bool IsHardwareStable)? ParseWeight(string rawData)
        {
            // Tìm cờ Ổn định (P+) và cờ Dao động (@+) từ cuối chuỗi lên
            int pIndex = rawData.LastIndexOf("P+");
            int aIndex = rawData.LastIndexOf("@+");

            // Lấy tín hiệu mới nhất trong Buffer
            int targetIndex = Math.Max(pIndex, aIndex);

            // Đảm bảo phía sau cờ có đủ 6 chữ số (VD: "P+031790" -> cần 8 ký tự)
            if (targetIndex != -1 && targetIndex + 8 <= rawData.Length)
            {
                // Cắt lấy 6 chữ số sau dấu +
                string weightStr = rawData.Substring(targetIndex + 2, 6);

                if (decimal.TryParse(weightStr, out decimal weight))
                {
                    // Nếu tín hiệu mới nhất là P+, nghĩa là phần cứng báo ĐÃ ỔN ĐỊNH
                    bool isStable = (targetIndex == pIndex);
                    return (weight, isStable);
                }
            }
            return null;
        }
    }
}
