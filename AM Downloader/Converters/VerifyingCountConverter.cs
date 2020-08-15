using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace AMDownloader
{
    class VerifyingCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int i_value;
            int.TryParse(value.ToString(), out i_value);
            if (i_value < 1)
            {
                return string.Empty;
            }
            else
            {
                return "Verifying " + i_value + " item(s)";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
