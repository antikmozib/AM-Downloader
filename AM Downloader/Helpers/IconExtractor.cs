// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AMDownloader
{
    public class Shell32
    {
        public const int MAX_PATH = 256;

        [StructLayout(LayoutKind.Sequential)]
        public struct SHITEMID
        {
            public ushort cb;

            [MarshalAs(UnmanagedType.LPArray)]
            public byte[] abID;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ITEMIDLIST
        {
            public SHITEMID mkid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;

            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszTitle;

            public uint ulFlags;
            public IntPtr lpfn;
            public int lParam;
            public IntPtr iImage;
        }

        // Browsing for directory.
        public const uint BIF_RETURNONLYFSDIRS = 0x0001;

        public const uint BIF_DONTGOBELOWDOMAIN = 0x0002;
        public const uint BIF_STATUSTEXT = 0x0004;
        public const uint BIF_RETURNFSANCESTORS = 0x0008;
        public const uint BIF_EDITBOX = 0x0010;
        public const uint BIF_VALIDATE = 0x0020;
        public const uint BIF_NEWDIALOGSTYLE = 0x0040;
        public const uint BIF_USENEWUI = (BIF_NEWDIALOGSTYLE | BIF_EDITBOX);
        public const uint BIF_BROWSEINCLUDEURLS = 0x0080;
        public const uint BIF_BROWSEFORCOMPUTER = 0x1000;
        public const uint BIF_BROWSEFORPRINTER = 0x2000;
        public const uint BIF_BROWSEINCLUDEFILES = 0x4000;
        public const uint BIF_SHAREABLE = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SHFILEINFO
        {
            public const int NAMESIZE = 80;
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NAMESIZE)]
            public string szTypeName;
        };

        public const uint SHGFI_ICON = 0x000000100;     // get icon
        public const uint SHGFI_DISPLAYNAME = 0x000000200;     // get display name
        public const uint SHGFI_TYPENAME = 0x000000400;     // get type name
        public const uint SHGFI_ATTRIBUTES = 0x000000800;     // get attributes
        public const uint SHGFI_ICONLOCATION = 0x000001000;     // get icon location
        public const uint SHGFI_EXETYPE = 0x000002000;     // return exe type
        public const uint SHGFI_SYSICONINDEX = 0x000004000;     // get system icon index
        public const uint SHGFI_LINKOVERLAY = 0x000008000;     // put a link overlay on icon
        public const uint SHGFI_SELECTED = 0x000010000;     // show icon in selected state
        public const uint SHGFI_ATTR_SPECIFIED = 0x000020000;     // get only specified attributes
        public const uint SHGFI_LARGEICON = 0x000000000;     // get large icon
        public const uint SHGFI_SMALLICON = 0x000000001;     // get small icon
        public const uint SHGFI_OPENICON = 0x000000002;     // get open icon
        public const uint SHGFI_SHELLICONSIZE = 0x000000004;     // get shell size icon
        public const uint SHGFI_PIDL = 0x000000008;     // pszPath is a pidl
        public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;     // use passed dwFileAttribute
        public const uint SHGFI_ADDOVERLAYS = 0x000000020;     // apply the appropriate overlays
        public const uint SHGFI_OVERLAYINDEX = 0x000000040;     // Get the index of the overlay

        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [DllImport("Shell32.dll")]
        public static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags
            );
    }

    /// <summary>
    /// Wraps necessary functions imported from User32.dll. Code courtesy of MSDN Cold Rooster Consulting example.
    /// </summary>
    public class User32
    {
        /// <summary>
        /// Provides access to function required to delete handle. This method is used internally
        /// and is not required to be called separately.
        /// </summary>
        /// <param name="hIcon">Pointer to icon handle.</param>
        /// <returns>N/A</returns>
        [DllImport("User32.dll")]
        public static extern int DestroyIcon(IntPtr hIcon);
    }

    internal static class IconExtractor
    {
        private static Dictionary<string, ImageSource> _icons = new Dictionary<string, ImageSource>();

        public static ImageSource Extract(string destination, string url, bool isDirectory)
        {
            Shell32.SHFILEINFO shinfo = new Shell32.SHFILEINFO();
            ImageSource imageSource;
            string alternatePath = destination;
            bool deleteFile = false;
            string ext = Path.GetExtension(destination);
            string shortFilename = Path.GetFileName(destination);
            Icon icon;

            // have we seen these before?
            if (isDirectory && _icons.ContainsKey(destination))
            {
                _icons.TryGetValue(destination, out imageSource);
                return imageSource;
            }
            else if (!isDirectory && ext != ".exe" && _icons.ContainsKey(ext))
            {
                _icons.TryGetValue(ext, out imageSource);
                return imageSource;
            }
            else if (!isDirectory && ext == ".exe" && _icons.ContainsKey(shortFilename))
            {
                _icons.TryGetValue(shortFilename, out imageSource);
                return imageSource;
            }

            // check paths and create temp files if necessary
            if (isDirectory && !Directory.Exists(destination))
            {
                alternatePath = AppDomain.CurrentDomain.BaseDirectory;
            }
            else if (!isDirectory && !File.Exists(destination))
            {
                ext = Path.GetExtension(destination).ToLower();
                alternatePath = Path.GetTempPath() + Path.DirectorySeparatorChar + "AMDownloader" + DateTime.Now.ToFileTimeUtc() + "." + ext;
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
                Shell32.SHGetFileInfo(
                    alternatePath,
                    Shell32.FILE_ATTRIBUTE_NORMAL,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    Shell32.SHGFI_ICON | Shell32.SHGFI_LARGEICON);

                icon = Icon.FromHandle(shinfo.hIcon);
            }

            // create the image from the icon
            imageSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                new Int32Rect(0, 0, icon.Width, icon.Height),
                BitmapSizeOptions.FromEmptyOptions());

            // save the keys and images
            if (isDirectory && !_icons.ContainsKey(alternatePath))
            {
                _icons.Add(alternatePath, imageSource);
            }
            else
            {
                if (ext != ".exe" && !_icons.ContainsKey(ext))
                {
                    _icons.Add(ext, imageSource);
                }
                else if (ext == ".exe" && File.Exists(destination))
                {
                    _icons.Add(shortFilename, imageSource);
                }
            }

            if (deleteFile)
            {
                File.Delete(alternatePath);
            }

            if (isDirectory)
            {
                User32.DestroyIcon(shinfo.hIcon);
            }

            return imageSource;
        }

        [SuppressUnmanagedCodeSecurity]
        internal static class SafeNativeMethods
        {
            [DllImport("shell32.dll", EntryPoint = "ExtractAssociatedIcon", CharSet = CharSet.Auto)]
            internal static extern IntPtr ExtractAssociatedIcon(HandleRef hInst, StringBuilder iconPath, ref int index);
        }
    }
}