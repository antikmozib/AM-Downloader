﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    internal class SettingsConnectionTimeoutConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return 0;
            }

            return ((long)value / 1000).ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return 0;
            }

            if (int.TryParse((string)value, out int speed))
            {
                return (long)speed * 1000;
            }
            else
            {
                return 0;
            }
        }
    }
}