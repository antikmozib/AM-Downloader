﻿// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class SettingsSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return 0;
            }

            return ((long)value / (int)Common.Constants.ByteConstants.KILOBYTE).ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return 0;
            }

            if (int.TryParse((string)value, out int speed))
            {
                return speed * (long)Common.Constants.ByteConstants.KILOBYTE;
            }
            else
            {
                return 0;
            }
        }
    }
}
