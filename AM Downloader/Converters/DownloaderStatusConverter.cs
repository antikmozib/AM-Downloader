// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return String.Empty;
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
