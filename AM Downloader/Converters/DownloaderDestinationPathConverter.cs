using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;
using AMDownloader.Common;

namespace AMDownloader
{
    class DownloaderDestinationPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = Path.GetDirectoryName(value.ToString());
            string output = "in ";

            if (Path.GetDirectoryName(path) == null)
            {
                // downloading to drive root
                output = "";
            }
            else if (Path.GetDirectoryName(Path.GetDirectoryName(path)) == null)
            {
                // downloading to a first-level folder in drive
                string parent = Path.GetDirectoryName(path);
                output += CommonFunctions.DriveLetterToName(parent);
            }
            else
            {
                // downloading to some other folder deep in the drive
                output += Path.GetDirectoryName(path);
            }

            return output;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
