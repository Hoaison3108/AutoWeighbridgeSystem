using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters
{
    /// <summary>
    /// Converter đảo ngược giá trị Boolean:
    /// True  => False
    /// False => True
    /// Dùng để bind IsEnabled ngược chiều với một bool property.
    /// Ví dụ: IsEnabled="{Binding IsVoid, Converter={StaticResource InvertBool}}"
    /// → nút bị disabled khi IsVoid=True, enabled khi IsVoid=False.
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }
}
