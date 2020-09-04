// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using AMDownloader.Common;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader
{
    internal class DownloaderDestinationFolderNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value.ToString();
            if (Path.GetDirectoryName(Path.GetDirectoryName(path)) == null)
            {
                // parent folder is root drive
                string parent = Path.GetDirectoryName(path);
                return CommonFunctions.DriveLetterToName(parent);
            }
            return Path.GetFileName(Path.GetDirectoryName(path));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}