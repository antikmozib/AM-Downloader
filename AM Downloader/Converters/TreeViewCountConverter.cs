// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    internal class TreeViewCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int.TryParse(value.ToString(), out int count);
            if (count == 0) return String.Empty;
            return count.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}