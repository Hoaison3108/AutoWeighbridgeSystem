using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoWeighbridgeSystem.Services.Protocols
{
    /// <summary>
    /// Giao thức Flintec FT111D: dữ liệu gửi liên tục qua Serial, mỗi frame là một dòng.
    /// </summary>
    /// <remarks>
    /// Định dạng frame (16 ký tự + CRLF):
    /// <code>
    ///   i[SS] [WWWWW]     [T]\r\n
    ///
    ///   i    — ký tự bắt đầu frame (cố định)
    ///   SS   — status byte (hex 2 ký tự):
    ///            00  → Ổn định  (Stable, no motion)
    ///            80  → Dao động (Unstable, motion detected)
    ///            (bit 7 = 1 → motion, bit 7 = 0 → stable)
    ///   [SP] — khoảng trắng phân tách
    ///   WWWWW — trọng lượng 5 chữ số (kg)
    ///   [5 SP] — padding (thường là giá trị tare = 0 hoặc khoảng trắng)
    ///   T    — field phụ (tare/mode), thường = 0
    /// </code>
    /// Ví dụ thực tế:
    /// <list type="bullet">
    ///   <item><c>i80 16360     0</c> — Dao động, 16360 kg</item>
    ///   <item><c>i00 16370     0</c> — Ổn định,  16370 kg</item>
    /// </list>
    ///
    /// <b>Cơ chế xác nhận ổn định 2 lớp:</b>
    /// <list type="number">
    ///   <item>Lớp 1 (Protocol): tích lũy <see cref="StabilityBufferSize"/> mẫu i00 liên tiếp,
    ///   delta trong ngưỡng <see cref="StabilityDeltaKg"/> → mới báo IsHardwareStable = true.</item>
    ///   <item>Lớp 2 (ScaleService): khi nhận IsHardwareStable = true → tin ngay, ghi nhận ổn định.</item>
    /// </list>
    /// Mục đích: bù đắp cho phần cứng FT111D có thể gửi flag i00 glitch đơn lẻ.
    /// </remarks>
    public class FlintecFT111Dprotocol : IScaleProtocol
    {
        public string ProtocolName => "FlintecFT111D";

        // =========================================================================
        // INTERFACE IMPLEMENTATION
        // =========================================================================

        /// <inheritdoc/>
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
        ///   <item>Quét ngược từ cuối buffer, tìm 'i' đứng đầu dòng.</item>
        ///   <item>Parse thủ công status byte và trọng lượng.</item>
        ///   <item>Trả về kết quả hwStableFlag trực tiếp cho ScaleService xử lý đệm.</item>
        /// </list>
        /// </remarks>
        public (decimal Weight, bool IsHardwareStable, string Remainder)? TryExtractFrame(string buffer)
        {
            if (string.IsNullOrEmpty(buffer))
                return null;

            int searchPos = buffer.Length - 1;

            while (searchPos >= 0)
            {
                // Tìm ký tự 'i' đứng đầu dòng (sau '\n', '\r', hoặc ở đầu buffer)
                int iPos = buffer.LastIndexOf('i', searchPos);
                if (iPos < 0) break;

                bool isLineStart = (iPos == 0)
                                || buffer[iPos - 1] == '\n'
                                || buffer[iPos - 1] == '\r';

                if (isLineStart)
                {
                    // Kiểm tra đủ 15 ký tự tối thiểu: i(1) + SS(2) + SP(1) + WWWWW(5) + SP×5(5) + T(1)
                    if (iPos + 15 <= buffer.Length)
                    {
                        char s1 = buffer[iPos + 1];
                        char s2 = buffer[iPos + 2];
                        char sp = buffer[iPos + 3];

                        if (IsHexChar(s1) && IsHexChar(s2) && sp == ' ')
                        {
                            string weightStr = buffer.Substring(iPos + 4, 5);

                            if (decimal.TryParse(weightStr, out decimal weight))
                            {
                                // Parse status byte: bit 7 = motion flag
                                int  statusVal    = (HexVal(s1) << 4) | HexVal(s2);
                                bool hwStableFlag = (statusVal & 0x80) == 0; // i00 → true, i80 → false

                                // Phần dư: bỏ qua CRLF sau frame
                                int frameEnd = iPos + 15;
                                while (frameEnd < buffer.Length &&
                                       (buffer[frameEnd] == '\r' || buffer[frameEnd] == '\n'))
                                    frameEnd++;

                                string remainder = buffer.Substring(frameEnd);
                                return (weight, hwStableFlag, remainder);
                            }
                        }
                    }
                }

                searchPos = iPos - 1;
            }

            return null;
        }

        // =========================================================================
        // HELPER — HEX PARSING
        // =========================================================================

        /// <summary>Kiểm tra ký tự có phải hex digit (0-9, A-F, a-f) không.</summary>
        private static bool IsHexChar(char c)
            => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        /// <summary>Chuyển hex digit thành giá trị int (0-15).</summary>
        private static int HexVal(char c)
            => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
    }
}
