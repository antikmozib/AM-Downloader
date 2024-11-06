// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AMDownloader.Helpers
{
    public static class Constants
    {
        public const long Kilobyte = 1024;

        public const long Megabyte = 1024 * Kilobyte;

        public const long Gigabyte = 1024 * Megabyte;

        public const int ParallelDownloadsLimit = 10;

        public const int ParallelConnPerDownloadLimit = 5;

        public const string TempDownloadExtension = ".AMDownload";

        public const string UpdateApiAddress = @"https://api.mozib.io/app-update/";

        public const string UpdateApiAppId = "amdownloader";

        /// <summary>
        /// Gets the path to the %LOCALAPPDATA% folder.
        /// </summary>
        public static readonly string LocalAppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetExecutingAssembly().GetName().Name);

        public static readonly string DownloadsHistoryFile = Path.Combine(LocalAppDataFolder, "History.json");

        public static readonly string SavedLocationsFile = Path.Combine(LocalAppDataFolder, "SavedLocations.json");

        public static readonly string UIColumnOrderFile = Path.Combine(LocalAppDataFolder, "UIColumnOrder.json");

        /// <summary>
        /// Gets the path to the %USERPROFILE%/Downloads folder.
        /// </summary>
        public static readonly string UserDownloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        
        public static readonly string LogFile = Path.Combine(LocalAppDataFolder, "logs", "log.log");
    }
}
