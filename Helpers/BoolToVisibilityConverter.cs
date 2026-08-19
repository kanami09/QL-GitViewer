using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickLook.Plugin.GitViewer.Helpers
{
    /// <summary>
    ///     bool 转 Visibility，并支持反转。框架自带的 BooleanToVisibilityConverter 会忽略
    ///     ConverterParameter，没法驱动"列表为空时才显示"的那几处占位文字。
    /// </summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool && (bool)value;

            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
                flag = !flag;

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
