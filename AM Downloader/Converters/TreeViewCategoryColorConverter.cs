// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AMDownloader
{
    internal class TreeViewCategoryColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = (Categories)value;
            switch (status)
            {
                case Categories.All:
                    return Application.Current.Resources["CategoryAllColor"];

                case Categories.Downloading:
                    return Application.Current.Resources["CategoryDownloadingColor"];

                case Categories.Queued:
                    return Application.Current.Resources["CategoryQueuedColor"];

                case Categories.Finished:
                    return Application.Current.Resources["CategoryFinishedColor"];

                case Categories.Ready:
                    return Application.Current.Resources["CategoryReadyColor"];

                case Categories.Errored:
                    return Application.Current.Resources["CategoryErroredColor"];

                case Categories.Verifying:
                    return Application.Current.Resources["CategoryVerifyingColor"];

                case Categories.Paused:
                    return Application.Current.Resources["CategoryPausedColor"];
            }
            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}