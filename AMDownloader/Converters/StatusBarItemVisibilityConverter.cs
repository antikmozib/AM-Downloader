﻿// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class StatusBarItemVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!string.IsNullOrWhiteSpace(value.ToString()))
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}