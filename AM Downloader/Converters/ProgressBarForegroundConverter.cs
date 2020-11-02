// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

using AMDownloader.ObjectModel;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AMDownloader
{
    internal class ProgressBarForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DownloadStatus status = (DownloadStatus)value;
            switch (status)
            {
                case DownloadStatus.Finished:
                    return Application.Current.Resources["ProgressBarFinishedForeground"];
            }
            return Application.Current.Resources["ProgressBarForeground"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}