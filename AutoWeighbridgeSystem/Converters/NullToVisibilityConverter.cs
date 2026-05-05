using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object VisibilityWhenNull { get; set; } = Visibility.Collapsed;
        public object VisibilityWhenNotNull { get; set; } = Visibility.Visible;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? VisibilityWhenNull : VisibilityWhenNotNull;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
