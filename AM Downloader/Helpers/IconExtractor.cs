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

        public static ImageSource Extract(string destination, bool isDirectory)
        {
            Native.Shell32.SHFILEINFO shFileInfo = new();
            ImageSource imageSource;
            string alternatePath = destination;
            bool deleteFile = false;
            string ext = Path.GetExtension(destination).ToLower();
            string shortFilename = Path.GetFileName(destination);
            Icon icon;

            // have we seen this item before?

            if (isDirectory && _iconRepo.ContainsKey(destination))
            {
                _iconRepo.TryGetValue(destination, out imageSource);

                return imageSource;
            }
            else if (!isDirectory && ext != ".exe" && _iconRepo.ContainsKey(ext))
            {
                _iconRepo.TryGetValue(ext, out imageSource);

                return imageSource;
            }
            else if (!isDirectory && ext == ".exe" && _iconRepo.ContainsKey(shortFilename))
            {
                _iconRepo.TryGetValue(shortFilename, out imageSource);

                return imageSource;
            }

            // we haven't seen this before;
            // check the paths and create the temp files if necessary

            if (isDirectory && !Directory.Exists(destination))
            {
                alternatePath = AppDomain.CurrentDomain.BaseDirectory;
            }
            else if (!isDirectory && !File.Exists(destination))
            {
                ext = Path.GetExtension(destination).ToLower();
                alternatePath = Path.Combine(Path.GetTempPath(), "AMDownloader" + DateTime.Now.ToFileTimeUtc() + ext);

                File.Create(alternatePath).Close();
                deleteFile = true;
            }

            // grab the actual icons

            if (!isDirectory)
            {
                // file icon

                icon = Icon.ExtractAssociatedIcon(alternatePath);
            }
            else
            {
                // folder icon

                Native.Shell32.SHGetFileInfo(alternatePath,
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

            if (isDirectory && !_iconRepo.ContainsKey(alternatePath))
            {
                _iconRepo.Add(alternatePath, imageSource);
            }
            else
            {
                if (ext != ".exe" && !_iconRepo.ContainsKey(ext))
                {
                    _iconRepo.Add(ext, imageSource);
                }
                else if (ext == ".exe" && File.Exists(destination))
                {
                    _iconRepo.Add(shortFilename, imageSource);
                }
            }

            if (deleteFile)
            {
                File.Delete(alternatePath);
            }

            if (isDirectory)
            {
                Native.User32.DestroyIcon(shFileInfo.hIcon);
            }

            return imageSource;
        }
    }
}