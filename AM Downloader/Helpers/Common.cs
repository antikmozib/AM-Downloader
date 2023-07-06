// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Properties;
using System;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;

namespace AMDownloader.Helpers
{
    public static class Common
    {
        public static class Constants
        {
            public const int ParallelDownloadsLimit = 10;
            public const int ParallelConnPerDownloadLimit = 5;
            public const string TempDownloadExtension = ".AMDownload";
            public const string UpdateServer = @"https://mozib.io/downloads/update.php";

            public enum ByteConstants
            {
                KILOBYTE = 1024,
                MEGABYTE = KILOBYTE * KILOBYTE,
                GIGABYTE = MEGABYTE * KILOBYTE
            }
        }

        public static class Paths
        {
            /// <summary>
            /// Gets the path to the %LOCALAPPDATA% folder.
            /// </summary>
            public static string LocalAppDataFolder =>
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetExecutingAssembly().GetName().Name);
            public static string DownloadsHistoryFile => Path.Combine(LocalAppDataFolder, "History.xml");
            public static string SavedLocationsFile => Path.Combine(LocalAppDataFolder, "SavedLocations.xml");
            public static string UIColumnOrderFile => Path.Combine(LocalAppDataFolder, "UIColumnOrder.xml");
            /// <summary>
            /// Gets the path to the %USERPROFILE%/Downloads folder.
            /// </summary>
            public static string UserDownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            public static string LogFile => Path.Combine(LocalAppDataFolder, "logs", "log.log");
        }

        public static class Functions
        {
            /// <summary>
            /// Extracts the filename from an URL.
            /// </summary>
            /// <param name="url">The URL to extract the filename from.</param>
            /// <returns>The extracted filename if the URL is valid, otherwise an empty string.</returns>
            public static string GetFileNameFromUrl(string url)
            {
                bool success = Uri.TryCreate(url, UriKind.Absolute, out Uri uri);

                if (success)
                {
                    return Path.GetFileName(uri.LocalPath);
                }
                else
                {
                    return string.Empty;
                }
            }

            /// <summary>
            /// Gets the drive type description, such as Local, Removable, Network or RAM Disk, from the drive root path.
            /// </summary>
            /// <param name="driveRootPath">The path to the root of the drive.</param>
            /// <returns>A description of the type of drive, such as Local, Removable, Network or RAM Disk.</returns>
            public static string GetDriveType(string driveRootPath)
            {
                var drives = DriveInfo.GetDrives();

                foreach (var drive in drives)
                {
                    if (drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) != driveRootPath.TrimEnd(Path.DirectorySeparatorChar))
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
                                return "Removable Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";

                            case DriveType.Network:
                                return "Network Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";

                            case DriveType.Ram:
                                return "RAM Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";

                            default:
                                return "Unknown Disk (" + drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + ")";
                        }
                    }
                    else
                    {
                        return drive.VolumeLabel;
                    }
                }

                return Path.GetPathRoot(driveRootPath);
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

            public static void Serialize<T>(T obj, string path)
            {
                var xmlSerializer = new XmlSerializer(typeof(T));
                using var streamWriter = new StreamWriter(path, false);

                try
                {
                    xmlSerializer.Serialize(streamWriter, obj);
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message, ex);
                }
            }

            public static T Deserialize<T>(string path)
            {
                var xmlSerializer = new XmlSerializer(typeof(T));
                using var streamReader = new StreamReader(path);

                try
                {
                    return (T)xmlSerializer.Deserialize(streamReader);
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message, ex);
                }
            }
        }
    }
}
