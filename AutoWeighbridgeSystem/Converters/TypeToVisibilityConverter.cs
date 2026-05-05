using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters
{
    public class TypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            Type valueType = value.GetType();
            Type targetViewType = parameter as Type;

            return valueType == targetViewType ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
