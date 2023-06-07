// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.QueueProcessing;
using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader
{
    internal class DownloaderStatusConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var queueProcessor = values[0] as QueueProcessor;
            var downloaderObject = values[1] as DownloaderObjectModel;

            return queueProcessor.IsQueued(downloaderObject) 
                && downloaderObject.Status == DownloadStatus.Ready 
                ? "Queued" 
                : downloaderObject.Status.ToString();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
