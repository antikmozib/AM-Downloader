// Copyright (C) 2020-2022 Antik Mozib. All rights reserved.

using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace AMDownloader.Common
{
    internal enum ByteConstants
    {
        KILOBYTE = 1024,
        MEGABYTE = KILOBYTE * KILOBYTE,
        GIGABYTE = MEGABYTE * KILOBYTE
    }

    internal static class AppConstants
    {
        public const int CollectionRefreshInterval = 1000; // 1 sec
        public const int RequestThrottlerInterval = 60000; // 1 min
        public const int DownloaderStreamBufferLength = (int)ByteConstants.KILOBYTE;
        public const int RemovingFileBytesBufferLength = (int)(ByteConstants.MEGABYTE);
        public const string DownloaderSplitedPartExtension = ".AMDownload";
        public const string DownloaderFileMagicString = "[AMDownload-Paused]";
        public const int ParallelDownloadsLimit = 10;
        public const int ParallelStreamsLimit = 5;
        public const string DocLink = "AM Downloader Help.chm";
        public const string UpdateLink = @"https://mozib.io/downloads/update.php";
    }

    internal static class AppPaths
    {
        public static string LocalAppData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetExecutingAssembly().GetName().Name);

        public static string DownloadsHistoryFile => Path.Combine(LocalAppData, "History.xml");
        public static string SavedLocationsFile => Path.Combine(LocalAppData, "SavedLocations.xml");
        public static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    internal static class CommonFunctions
    {
        public static string GetFreshFilename(string path)
        {
            string dirName = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);
            string result = dirName + Path.DirectorySeparatorChar + fileName;
            int i = 0;

            while (File.Exists(result) || File.Exists(result + AppConstants.DownloaderSplitedPartExtension))
            {
                result = dirName + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(fileName) +
                    " (" + ++i + ")" + Path.GetExtension(fileName);
            };

            return result;
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
    }

    internal static class AppUpdateService
    {
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