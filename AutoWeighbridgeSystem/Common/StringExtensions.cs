using System.Text.RegularExpressions;

namespace AutoWeighbridgeSystem.Common
{
    public static class StringExtensions
    {
        /// <summary>
        /// Chuẩn hóa biển số xe Việt Nam. Loại bỏ khoảng trắng, dấu phẩy, dấu chấm.
        /// Chèn dấu '-' vào giữa phần định danh (Mã tỉnh, sê ri) và phần biển số.
        /// Hỗ trợ cả định dạng "86C-12345" (3 ký tự đầu) và "86LD-1234" hoặc "86C1-1234" (4 ký tự đầu).
        /// </summary>
        public static string FormatLicensePlate(this string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            
            // Cạo sạch mảng nhiễu, đẩy về IN HOA
            string raw = input.ToUpper().Replace(" ", "").Replace(".", "").Replace("-", "");
            
            // Regex tối ưu cho biển số VN (2 số + 1,2 chữ cái + có thể 1 số) + (4,5 số đuôi)
            var match = Regex.Match(raw, @"^([0-9]{2}[A-Z]{1,2}[0-9]?)([0-9]{4,5})$");
            if (match.Success)
            {
                return $"{match.Groups[1].Value}-{match.Groups[2].Value}";
            }
            
            // Dự phòng Fallback (đúng mong đợi của user luôn cắt sau 3 ký tự nếu độ dài trên 7)
            if (raw.Length >= 6)
            {
                int cutIndex = 3;
                // Nếu biển LD/KT có tới 4 ký tự mào đầu (vd: 86LD1234)
                if (raw.Length > 3 && char.IsLetter(raw[3]) && !char.IsLetter(raw[2]))
                {
                    cutIndex = 4;
                }
                else if (raw.Length > 3 && raw.Substring(0, 4).Any(char.IsLetter))
                {
                   // Do heuristic check
                   if (char.IsLetter(raw[2])) cutIndex = 3;
                }

                return $"{raw.Substring(0, cutIndex)}-{raw.Substring(cutIndex)}";
            }

            return raw;
        }
    }
}
