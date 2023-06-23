// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AMDownloader.Converters
{
    internal class DownloaderFileIconConverter : IValueConverter
    {
        private Dictionary<string, ImageSource> _icons = new Dictionary<string, ImageSource>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var item = value as DownloaderObjectModel;
            return IconExtractor.Extract(item.Destination, false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}