using AutoWeighbridgeSystem.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoWeighbridgeSystem.Converters
{
    /// <summary>
    /// Chuyển đổi <see cref="HardwareConnectionStatus"/> thành màu sắc cho indicator dot.
    /// <list type="bullet">
    ///   <item>Online      → #00E676 (xanh lá)</item>
    ///   <item>Connecting  → #FFEB3B (vàng nhạt)</item>
    ///   <item>Reconnecting→ #FF9800 (cam)</item>
    ///   <item>Offline     → #E53935 (đỏ)</item>
    /// </list>
    /// </summary>
    [ValueConversion(typeof(HardwareConnectionStatus), typeof(SolidColorBrush))]
    public class HardwareStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HardwareConnectionStatus status)
            {
                return status switch
                {
                    HardwareConnectionStatus.Online       => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")),
                    HardwareConnectionStatus.Connecting   => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEB3B")),
                    HardwareConnectionStatus.Reconnecting => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800")),
                    HardwareConnectionStatus.Offline      => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935")),
                    HardwareConnectionStatus.Disabled     => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
                    _                                     => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
