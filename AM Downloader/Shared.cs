﻿using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using System.Collections.ObjectModel;
using System.Windows;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace AMDownloader
{
    class Shared
    {
        public const long KILOBYTE = 1024;
        public const long MEGABYTE = KILOBYTE * KILOBYTE;
        public const long GIGABYTE = MEGABYTE * KILOBYTE;
        public const long TERABYTE = GIGABYTE * KILOBYTE;

        public static string PrettyNum<T>(T num)
        {
            if (num == null)
            {
                return string.Empty;
            }

            double result = 0;
            double.TryParse(num.ToString(), out result);

            if (result > GIGABYTE)
            {
                result = Math.Round(result / GIGABYTE, 3);
                return result.ToString("#0.000") + " GB";
            }
            else if (result > MEGABYTE)
            {
                result = Math.Round(result / MEGABYTE, 2);
                return result.ToString("#0.00") + " MB";
            }
            else if (result > KILOBYTE)
            {
                result = Math.Round(result / KILOBYTE, 0);
                return result.ToString() + " KB";
            }
            else
            {
                return result.ToString() + " B";
            }
        }

        public static string GetValidFilename(string defaultFilename)
        {
            string _path = Path.GetDirectoryName(defaultFilename);
            string _filename = Path.GetFileName(defaultFilename);
            string result = _path + Path.DirectorySeparatorChar + _filename;

            int i = 0;
            while (File.Exists(result))
            {
                result = _path + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(_filename) + " (" + ++i + ")" + Path.GetExtension(_filename);
            };

            return result;
        }
    }
}
