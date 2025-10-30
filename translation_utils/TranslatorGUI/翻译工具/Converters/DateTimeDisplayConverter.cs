using System;
using System.Globalization;
using System.Windows.Data;

namespace 翻译工具
{
    // 将默认/无效的 DateTime 显示为空字符串，正常格式为 yyyy-MM-dd HH:mm:ss
    public class DateTimeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
            {
                if (dt == default || dt == DateTime.MinValue)
                    return string.Empty;
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 简单容错：空串 -> 默认时间；其余尝试解析
            if (value is string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return default(DateTime);
                if (DateTime.TryParse(s, culture, DateTimeStyles.None, out var dt))
                    return dt;
            }
            return default(DateTime);
        }
    }
}
