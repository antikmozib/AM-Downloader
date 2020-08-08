using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DateTime date = DateTime.Parse(value.ToString());
            return date.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
