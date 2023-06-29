// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AMDownloader.Helpers
{
    internal static class IconExtractor
    {
        private static readonly Dictionary<string, ImageSource> _iconRepo = new();

        public static ImageSource Extract(string path, bool isDirectory)
        {
            ImageSource imageSource;
            Icon icon;
            Native.Shell32.SHFILEINFO shFileInfo = new();
            string tempPath = path.ToLower();
            string shortFileName = Path.GetFileName(path).ToLower();
            string ext = Path.GetExtension(path).ToLower();
            bool deleteFile = false;

            // have we seen this item before?
            if (isDirectory && _iconRepo.ContainsKey(path))
            {
                _iconRepo.TryGetValue(path, out imageSource);

                return imageSource;
            }
            else if (!isDirectory && ext != ".exe" && _iconRepo.ContainsKey(ext))
            {
                _iconRepo.TryGetValue(ext, out imageSource);

                return imageSource;
            }
            else if (!isDirectory && ext == ".exe" && _iconRepo.ContainsKey(shortFileName))
            {
                _iconRepo.TryGetValue(shortFileName, out imageSource);

                return imageSource;
            }

            // we haven't seen this before;
            // check the paths and create the temp files if necessary
            if (isDirectory && !Directory.Exists(path))
            {
                tempPath = AppDomain.CurrentDomain.BaseDirectory;
            }
            else if (!isDirectory && !File.Exists(path))
            {
                ext = Path.GetExtension(path).ToLower();
                tempPath = Path.Combine(Path.GetTempPath(), "AMDownloader" + DateTime.Now.ToFileTimeUtc() + ext);

                File.Create(tempPath).Close();
                deleteFile = true;
            }

            // grab the actual icons
            if (!isDirectory)
            {
                // file icon
                icon = Icon.ExtractAssociatedIcon(tempPath);
            }
            else
            {
                // folder icon

                Native.Shell32.SHGetFileInfo(tempPath,
                    Native.Shell32.FILE_ATTRIBUTE_NORMAL,
                    ref shFileInfo,
                    (uint)Marshal.SizeOf(shFileInfo),
                    Native.Shell32.SHGFI_ICON | Native.Shell32.SHGFI_LARGEICON);

                icon = Icon.FromHandle(shFileInfo.hIcon);
            }

            // create the image from the icon
            imageSource = Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                new Int32Rect(0, 0, icon.Width, icon.Height),
                BitmapSizeOptions.FromEmptyOptions());

            // save the keys and images
            if (isDirectory && !_iconRepo.ContainsKey(tempPath))
            {
                _iconRepo.Add(tempPath, imageSource);
            }
            else
            {
                if (ext != ".exe" && !_iconRepo.ContainsKey(ext))
                {
                    _iconRepo.Add(ext, imageSource);
                }
                else if (ext == ".exe" && File.Exists(path))
                {
                    _iconRepo.Add(shortFileName, imageSource);
                }
            }

            if (deleteFile)
            {
                File.Delete(tempPath);
            }

            if (isDirectory)
            {
                Native.User32.DestroyIcon(shFileInfo.hIcon);
            }

            return imageSource;
        }
    }
}