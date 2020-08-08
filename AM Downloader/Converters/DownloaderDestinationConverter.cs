using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderDestinationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string dest = value.ToString();
            return new FileInfo(dest).Directory.Name + " (" + dest.Substring(0, dest.Length - Path.GetFileName(dest).Length - 1) + ")";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
