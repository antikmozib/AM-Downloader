using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderEtaConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return String.Empty;
            try
            {
                double.TryParse(value.ToString(), out double remaining);
                TimeSpan t = TimeSpan.FromMilliseconds(remaining);
                return t.Minutes + "m " + t.Seconds + "s";
            }
           catch
           {
               return String.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
