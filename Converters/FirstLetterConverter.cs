using System;
using System.Globalization;
using System.Windows.Data;

namespace Switch.Converters
{
    public class FirstLetterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return "?";
            return s.Substring(0, 1).ToUpperInvariant();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
