// Copyright (C) 2020 Antik Mozib. Released under CC BY-NC-SA 4.0.

using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader
{
    internal class DownloaderTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string extensionName;
            string path = value.ToString();
            extensionName = (string)Registry.GetValue("HKEY_CLASSES_ROOT\\" + Path.GetExtension(path), "", Path.GetExtension(path));
            return (string)Registry.GetValue("HKEY_CLASSES_ROOT\\" + extensionName, "", Path.GetExtension(path));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}