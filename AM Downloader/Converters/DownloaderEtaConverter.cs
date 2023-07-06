// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Globalization;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class DownloaderEtaConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            try
            {
                double.TryParse(value.ToString(), out double remaining);
                if (remaining > 0 && !double.IsInfinity(remaining))
                {
                    TimeSpan t = TimeSpan.FromMilliseconds(remaining);
                    if ((int)t.TotalHours == 0)
                    {
                        return t.Minutes + "m " + t.Seconds + "s";
                    }
                    return (int)t.TotalHours + "h " + t.Minutes + "m " + t.Seconds + "s";
                }
            }
            catch
            {
                return string.Empty;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}