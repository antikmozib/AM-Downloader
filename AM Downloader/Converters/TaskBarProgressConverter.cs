// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    internal class TaskBarProgressConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double d_progress;
            int.TryParse(value.ToString(), out int i_progress);
            d_progress = (double)i_progress / 100;
            return d_progress;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}