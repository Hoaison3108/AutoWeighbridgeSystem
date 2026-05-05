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
            string raw = input.ToUpper().Replace(" ", "").Replace(".", "").Replace("-", "").Replace("_", "");

            if (raw.Length < 4) return raw; // Chuỗi quá ngắn không thể định dạng

            // LUẬT ÉP BUỘC TUYỆT ĐỐI THEO YÊU CẦU: XXX-XXXX(X)
            // Luôn luôn lấy đúng 3 ký tự đầu tiên làm tiền tố, không cần regex rườm rà.
            return $"{raw.Substring(0, 3)}-{raw.Substring(3)}";
        }
    }
}
