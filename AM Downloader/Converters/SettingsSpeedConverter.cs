// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    internal class SettingsSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return 0;
            }

            return ((long)value / (int)Constants.ByteConstants.KILOBYTE).ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return 0;
            }

            if (int.TryParse((string)value, out int speed))
            {
                return speed * (long)Constants.ByteConstants.KILOBYTE;
            }
            else
            {
                return 0;
            }
        }
    }
}
