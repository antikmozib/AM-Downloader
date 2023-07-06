// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AMDownloader.Models;

namespace AMDownloader.Converters
{
    public class ProgressBarVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DownloadStatus status = (DownloadStatus)value;
            switch (status)
            {
                case DownloadStatus.Ready:
                    return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}