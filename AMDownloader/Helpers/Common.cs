// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using AMDownloader.Properties;
using System;
using System.IO;
using System.Text.Json;

namespace AMDownloader.Helpers
{
    public static class Common
    {/// <summary>
     /// Extracts the filename from an URL.
     /// </summary>
     /// <param name="url">The URL to extract the filename from.</param>
     /// <returns>The extracted filename if the URL is valid, otherwise an empty string.</returns>
        public static string GetFileNameFromUrl(string url)
        {
            bool success = Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri uri);

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

            if (Directory.Exists(Constants.LocalAppDataFolder))
            {
                try
                {
                    File.Delete(Constants.SavedLocationsFile);
                    File.Delete(Constants.UIColumnOrderFile);
                }
                catch
                {

                }
            }
        }

        public static void Serialize<T>(T obj, string outputFile)
        {
            var dirName = Path.GetDirectoryName(outputFile);

            try
            {
                if (!Directory.Exists(dirName) && dirName.Length > 0)
                {
                    Directory.CreateDirectory(dirName);
                }

                using var fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write);

                JsonSerializer.Serialize(fileStream, obj, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
        }

        public static T Deserialize<T>(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);

                return JsonSerializer.Deserialize<T>(fs);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
