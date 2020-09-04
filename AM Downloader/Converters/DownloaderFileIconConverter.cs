// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using AMDownloader.ObjectModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AMDownloader
{
    internal class DownloaderFileIconConverter : IValueConverter
    {
        private Dictionary<string, ImageSource> _icons = new Dictionary<string, ImageSource>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var item = value as DownloaderObjectModel;
            return IconExtractor.Extract(item.Destination, item.Url, false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}