using System;
using System.Globalization;
using System.Windows.Data;

namespace 翻译工具
{
    // 将默认/无效的 DateTime 显示为空字符串，否则格式化为 yyyy-MM-dd HH:mm:ss
    public class DateTimeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
            {
                if (dt == default || dt == DateTime.MinValue)
                    return string.Empty;

                // 可选自定义格式
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 仅用于显示，支持简单回写：空串->默认时间；否则尝试解析
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
