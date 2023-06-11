// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Properties;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace AMDownloader.Common
{
    internal static class Constants
    {
        public const int RequestThrottlerInterval = 60000; // 1 min
        public const string DownloaderSplitedPartExtension = ".AMDownload";
        public const int ParallelDownloadsLimit = 10;

        public enum ByteConstants
        {
            KILOBYTE = 1024,
            MEGABYTE = KILOBYTE * KILOBYTE,
            GIGABYTE = MEGABYTE * KILOBYTE
        }
    }

    internal static class Paths
    {
        /// <summary>
        /// Gets the path to the folder where to save the user-specific settings.
        /// </summary>
        public static string LocalAppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetExecutingAssembly().GetName().Name);
        public static string DownloadsHistoryFile => Path.Combine(LocalAppDataFolder, "History.xml");
        public static string SavedLocationsFile => Path.Combine(LocalAppDataFolder, "SavedLocations.xml");
        public static string UIColumnOrderFile => Path.Combine(LocalAppDataFolder, "UIColumnOrder.xml");
        public static string UserDownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    internal static class Functions
    {
        public static string GetNewFileName(string fullPath)
        {
            string dirName = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            string result = dirName + Path.DirectorySeparatorChar + fileName;
            int i = 0;

            while (File.Exists(result) || File.Exists(result + Constants.DownloaderSplitedPartExtension))
            {
                result = dirName + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(fileName) +
                    " (" + ++i + ")" + Path.GetExtension(fileName);
            };

            return result;
        }

        public static string GetFilenameFromUrl(string url)
        {
            Uri.TryCreate(url, UriKind.Absolute, out Uri uri);
            return Path.GetFileName(uri.LocalPath);
        }

        public static string DriveLetterToName(string rootPath)
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) != rootPath.TrimEnd(Path.DirectorySeparatorChar))
                {
                    continue;
                }

                if (drive.VolumeLabel == "")
                {
                    switch (drive.DriveType)
                    {
                        case DriveType.Fixed:
                            return "Local Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";

                        case DriveType.CDRom:
                        case DriveType.Removable:
                            return "Removeable Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";

                        case DriveType.Network:
                            return "Network Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";

                        case DriveType.Unknown:
                        case DriveType.Ram:
                            return "Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";
                    }
                }
                else
                {
                    return drive.VolumeLabel;
                }
            }

            return string.Empty;
        }

        public static void ResetAllSettings()
        {
            Settings.Default.Reset();
            if (Directory.Exists(Paths.LocalAppDataFolder))
            {
                try
                {
                    File.Delete(Paths.SavedLocationsFile);
                    File.Delete(Paths.UIColumnOrderFile);
                }
                catch
                {
                }
            }
        }
    }

    internal static class AppUpdateService
    {
        public const string UpdateServer = @"https://mozib.io/downloads/update.php";

        public static async Task<string> GetUpdateUrl(string url, string appName, string appVersion)
        {
            using var c = new HttpClient();
            appName = appName.Replace(" ", ""); // replace spaces in url
            try
            {
                var response = await c.GetAsync(url + "?appname=" + appName + "&version=" + appVersion);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}