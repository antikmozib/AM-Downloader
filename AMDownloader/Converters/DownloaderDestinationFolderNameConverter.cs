// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class DownloaderDestinationFolderNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value.ToString();
            if (Path.GetDirectoryName(Path.GetDirectoryName(path)) == null)
            {
                // Parent folder is root drive.
                string parent = Path.GetDirectoryName(path);
                return Common.GetDriveType(parent);
            }
            return Path.GetFileName(Path.GetDirectoryName(path));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}