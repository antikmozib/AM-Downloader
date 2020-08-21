using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return String.Empty;

            double l_val;
            double.TryParse(value.ToString(), out l_val);

            if (l_val > 1000000000)
            {
                return Math.Round(l_val / (1024 * 1024 * 1024), 3).ToString("#0.000") + " GB";
            }
            else if (l_val > 1000000)
            {
                return Math.Round(l_val / (1024 * 1024), 2).ToString("#0.00") + " MB";
            }
            else if (l_val > 1024)
            {
                return Math.Round(l_val / 1024, 0).ToString() + " KB";
            }
            else if (l_val > 0)
            {
                return l_val.ToString() + " B";
            }
            else
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
