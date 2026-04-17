using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services.Protocols
{
    /// <summary>
    /// Giao thức Vishay VT-220: dữ liệu gửi liên tục qua Serial.
    /// Mỗi frame có dạng: <c>P+031790</c> (ổn định) hoặc <c>@+031790</c> (dao động).
    /// <list type="bullet">
    /// <item><b>P+</b>: phần cứng báo ổn định (Stable)</item>
    /// <item><b>@+</b>: phần cứng báo đang dao động (Unstable)</item>
    /// <item><b>6 chữ số</b> sau dấu <c>+</c>: giá trị cân (kg)</item>
    /// </list>
    /// </summary>
    public class VishayVT220protocol : IScaleProtocol
    {
        public string ProtocolName => "VishayVT220";

        /// <inheritdoc/>
        /// <remarks>Giữ lại để tương thích ngược — delegate sang <see cref="TryExtractFrame"/>.</remarks>
        public (decimal Weight, bool IsHardwareStable)? ParseWeight(string rawData)
        {
            var result = TryExtractFrame(rawData);
            if (result.HasValue)
                return (result.Value.Weight, result.Value.IsHardwareStable);
            return null;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Thuật toán:
        /// <list type="number">
        /// <item>Tìm vị trí cuối cùng của <c>"P+"</c> và <c>"@+"</c> trong buffer.</item>
        /// <item>Chọn vị trí lớn hơn (frame mới nhất).</item>
        /// <item>Kiểm tra đủ 8 ký tự từ vị trí đó (<c>P+</c> + 6 chữ số).</item>
        /// <item>Parse 6 chữ số thành số thập phân.</item>
        /// <item>Trả về <c>Remainder</c> = phần sau frame (giữ lại cho lần append tiếp theo).</item>
        /// </list>
        /// </remarks>
        public (decimal Weight, bool IsHardwareStable, string Remainder)? TryExtractFrame(string buffer)
        {
            // Tìm frame hợp lệ từ cuối lên — lấy tín hiệu mới nhất trong buffer
            int pIndex      = buffer.LastIndexOf("P+");
            int aIndex      = buffer.LastIndexOf("@+");
            int targetIndex = Math.Max(pIndex, aIndex);

            // Chưa đủ frame hoàn chỉnh
            if (targetIndex == -1 || targetIndex + 8 > buffer.Length)
                return null;

            // Cắt lấy 6 chữ số sau cờ (ví dụ: "P+031790" → "031790")
            string weightStr = buffer.Substring(targetIndex + 2, 6);

            if (!decimal.TryParse(weightStr, out decimal weight))
                return null;

            bool isHardwareStable = (targetIndex == pIndex); // P+ → ổn định, @+ → dao động

            // Phần dư: byte chưa xử lý sau frame 8 ký tự
            string remainder = buffer.Substring(targetIndex + 8);

            return (weight, isHardwareStable, remainder);
        }
    }
}
