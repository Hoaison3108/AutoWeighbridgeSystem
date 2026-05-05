using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters // Namespace phải khớp 100% với XAML
{
    public class StringToVisibilityConverter : IValueConverter // Phải có 'public'
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status && !string.IsNullOrWhiteSpace(status))
            {
                // Ẩn overlay nếu status là các chuỗi báo Online bình thường
                if (status.StartsWith("Camera Online", StringComparison.OrdinalIgnoreCase))
                    return Visibility.Collapsed;
                    
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}