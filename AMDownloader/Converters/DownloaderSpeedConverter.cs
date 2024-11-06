// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class DownloaderSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            long? speed = (long)value;

            if (speed > 1000000)
            {
                return ((double)speed / (long)Constants.Megabyte).ToString("#0.00") + " MB/s";
            }
            else
            {
                if (speed == null)
                {
                    return string.Empty;
                }
                else
                {
                    return ((double)speed / (long)Constants.Kilobyte).ToString("#0") + " KB/s";
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}