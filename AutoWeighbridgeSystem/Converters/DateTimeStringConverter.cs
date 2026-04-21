using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters
{
    public class DateTimeStringConverter : IValueConverter
    {
        private const string DefaultFormat = "dd/MM/yyyy HH:mm:ss";

        /// <summary>
        /// Chuyển từ DateTime (Source) sang String (Target) để hiển thị lên TextBox
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
            {
                return dt.ToString(DefaultFormat);
            }
            return string.Empty;
        }

        /// <summary>
        /// Chuyển từ String (Target - User nhập) sang DateTime (Source) để lưu vào ViewModel
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = value as string;
            if (string.IsNullOrWhiteSpace(strValue)) return null;

            // Thử parse chính xác theo định dạng yêu cầu
            if (DateTime.TryParseExact(strValue, DefaultFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            // Nếu parse thất bại, trả về DependencyProperty.UnsetValue hoặc ném lỗi để WPF Validation bắt được
            return System.Windows.DependencyProperty.UnsetValue;
        }
    }
}
