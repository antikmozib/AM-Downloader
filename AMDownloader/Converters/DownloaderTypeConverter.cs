﻿// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace AMDownloader.Converters
{
    public class DownloaderTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value.ToString();
            var ext = Path.GetExtension(path); // e.g. .exe
            var extName = (string)Registry.GetValue("HKEY_CLASSES_ROOT\\" + ext, "", ext); // e.g. exefile
            var extDescription = (string)Registry.GetValue("HKEY_CLASSES_ROOT\\" + extName, "", ext); // e.g. Application

            return extDescription == ext
                ? $"{ext.Replace(".", "").ToUpper()}{(ext.Length == 0 ? "" : " ")}File"
                : extDescription;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}