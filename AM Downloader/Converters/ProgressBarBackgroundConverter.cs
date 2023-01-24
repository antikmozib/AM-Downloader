﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ObjectModel;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AMDownloader
{
    internal class ProgressBarBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DownloadStatus status = (DownloadStatus)value;
            switch (status)
            {
                case DownloadStatus.Error:
                    return Application.Current.Resources["ProgressBarErroredBackground"];
            }
            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}