using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters
{
    /// <summary>
    /// Converter đảo ngược logic Boolean: 
    /// True  => Collapsed (Ẩn)
    /// False => Visible (Hiện)
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Nếu là True (Auto Mode) thì trả về Collapsed để ẩn các nút Manual
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return false;
        }
    }
}