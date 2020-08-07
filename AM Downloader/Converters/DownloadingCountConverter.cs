using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    public class DownloadingCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int i_value;
            int.TryParse(value.ToString(), out i_value);
            if (i_value < 1)
            {
                return string.Empty;
            }
            else if (i_value == 1)
            {
                return i_value + " item downloading\t";
            }
            else
            {
                return i_value + " items downloading\t";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
