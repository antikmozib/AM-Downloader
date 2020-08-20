using AMDownloader.Common;
using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    public class DownloaderSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return String.Empty;

            long? speed = (long)value;

            if (speed > 1000000)
            {
                return ((double)speed / (long)ByteConstants.MEGABYTE).ToString("#0.00") + " MB/s";
            }
            else
            {
                if (speed == null)
                {
                    return String.Empty;
                }
                else
                {
                    return ((double)speed / (long)ByteConstants.KILOBYTE).ToString("#0") + " KB/s";
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
