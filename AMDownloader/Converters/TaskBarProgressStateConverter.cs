﻿// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;

namespace AMDownloader.Converters
{
    public class TaskBarProgressStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int.TryParse(value.ToString(), out int progress);
            if (progress > 0 && progress < 100)
            {
                return TaskbarItemProgressState.Normal;
            }
            else
            {
                return TaskbarItemProgressState.None;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}