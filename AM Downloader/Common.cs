using System;
using System.IO;
using System.Reflection;

namespace AMDownloader
{
    static class Common
    {
        public const long KILOBYTE = 1024;
        public const long MEGABYTE = KILOBYTE * KILOBYTE;
        public const long GIGABYTE = MEGABYTE * KILOBYTE;
        public const long TERABYTE = GIGABYTE * KILOBYTE;

        public static class ApplicationPaths
        {
            public static string LocalAppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetExecutingAssembly().GetName().Name);
            public static string DownloadsHistoryFile => Path.Combine(LocalAppData, "history.xml");
            public static string SavedLocationsFile => Path.Combine(LocalAppData, "savedlocations.xml");
            public static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        public static string GetValidFilename(string defaultFilename)
        {
            string path = Path.GetDirectoryName(defaultFilename);
            string filename = Path.GetFileName(defaultFilename);
            string result = path + Path.DirectorySeparatorChar + filename;
            int i = 0;

            while (File.Exists(result))
            {
                result = path + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(filename) + " (" + ++i + ")" + Path.GetExtension(filename);
            };

            return result;
        }
    }
}
