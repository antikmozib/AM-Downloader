// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

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
    public static class IconUtils
    {
        private static readonly Dictionary<string, ImageSource> _iconRepo = new();

        public static ImageSource ExtractFromFile(string path, bool isDirectory)
        {
            ImageSource imageSource;
            Icon icon;
            Native.Shell32.SHFILEINFO shFileInfo = new();
            string shortFileName = Path.GetFileName(path).ToLower();
            string ext = Path.GetExtension(path).ToLower();
            path = path.ToLower();

            // Have we seen this item before?
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

            uint fileAttributes;
            uint flags = Native.Shell32.SHGFI_USEFILEATTRIBUTES
                | Native.Shell32.SHGFI_ICON
                | Native.Shell32.SHGFI_LARGEICON;
            if (isDirectory)
            {
                fileAttributes = Native.Shell32.FILE_ATTRIBUTE_DIRECTORY;

                if (Directory.Exists(path))
                {
                    flags = Native.Shell32.SHGFI_ICON | Native.Shell32.SHGFI_LARGEICON;
                }
            }
            else
            {
                fileAttributes = Native.Shell32.FILE_ATTRIBUTE_NORMAL;
            }

            // Grab the actual icons.
            Native.Shell32.SHGetFileInfo(
                pszPath: path,
                dwFileAttributes: fileAttributes,
                psfi: ref shFileInfo,
                cbFileInfo: (uint)Marshal.SizeOf(shFileInfo),
                uFlags: flags);

            icon = Icon.FromHandle(shFileInfo.hIcon);

            // Create the image from the icon.
            imageSource = Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                new Int32Rect(0, 0, icon.Width, icon.Height),
                BitmapSizeOptions.FromEmptyOptions());

            // Save the keys and images.
            if (isDirectory)
            {
                _iconRepo.Add(path, imageSource);
            }
            else
            {
                if (ext != ".exe")
                {
                    _iconRepo.Add(ext, imageSource);
                }
                else if (ext == ".exe")
                {
                    _iconRepo.Add(shortFileName, imageSource);
                }
            }

            // Cleanup.
            Native.User32.DestroyIcon(shFileInfo.hIcon);

            return imageSource;
        }
    }
}