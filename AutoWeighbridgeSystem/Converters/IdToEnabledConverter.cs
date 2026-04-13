using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoWeighbridgeSystem.Converters
{
    // Converter này trả về True nếu Id = 0 (đang tạo mới), ngược lại trả về False (đang sửa)
    public class IdToEnabledConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int id)
            {
                return id == 0; // Chỉ cho phép nhập/sửa nếu là User mới (Id = 0)
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}