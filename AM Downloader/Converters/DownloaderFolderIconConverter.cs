// Copyright (C) 2020 Antik Mozib. Released under CC BY-NC-SA 4.0.

using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader
{
    internal class DownloaderFolderIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return IconExtractor.Extract(Path.GetDirectoryName(value.ToString()), String.Empty, true);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}