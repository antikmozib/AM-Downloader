using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return IconManager.FindIconForFilename(value.ToString(), false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
