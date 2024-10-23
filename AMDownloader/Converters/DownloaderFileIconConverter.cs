// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class DownloaderFileIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var item = value as DownloaderObjectModel;
            return IconUtils.ExtractFromFile(item.Destination, false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}