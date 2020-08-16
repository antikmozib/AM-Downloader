using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderDestinationPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string dest = value.ToString();
            string folder = Path.GetDirectoryName(Path.GetDirectoryName(dest));
            if (folder == "")
            {
                folder = Path.GetDirectoryName(dest);
            }
            return folder;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
