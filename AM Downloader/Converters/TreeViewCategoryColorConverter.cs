// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

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
            var status = (Category)value;
            switch (status)
            {
                case Category.All:
                    return Application.Current.Resources["CategoryAllColor"];

                case Category.Downloading:
                    return Application.Current.Resources["CategoryDownloadingColor"];

                case Category.Queued:
                    return Application.Current.Resources["CategoryQueuedColor"];

                case Category.Finished:
                    return Application.Current.Resources["CategoryFinishedColor"];

                case Category.Ready:
                    return Application.Current.Resources["CategoryReadyColor"];

                case Category.Errored:
                    return Application.Current.Resources["CategoryErroredColor"];

                case Category.Verifying:
                    return Application.Current.Resources["CategoryVerifyingColor"];

                case Category.Paused:
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