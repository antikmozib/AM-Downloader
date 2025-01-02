// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class ProgramStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}