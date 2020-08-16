using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    public class QueuedCountConverter : IValueConverter
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
                return "Queued: "+ i_value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
