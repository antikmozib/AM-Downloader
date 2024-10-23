// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class DownloaderDestinationPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = Path.GetDirectoryName(value.ToString());
            string output = "in ";

            if (Path.GetDirectoryName(path) == null)
            {
                // Downloading to drive root.
                output = "";
            }
            else if (Path.GetDirectoryName(Path.GetDirectoryName(path)) == null)
            {
                // Downloading to a first-level folder in drive.
                string parent = Path.GetDirectoryName(path);
                output += Common.Functions.GetDriveType(parent);
            }
            else
            {
                // Downloading to some other folder deep in the drive.
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