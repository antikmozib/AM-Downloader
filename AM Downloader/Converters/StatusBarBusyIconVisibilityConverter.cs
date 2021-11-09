// Copyright (C) 2020-2021 Antik Mozib.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AMDownloader
{
    internal class StatusBarBusyIconVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value.ToString() == "Ready")
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}