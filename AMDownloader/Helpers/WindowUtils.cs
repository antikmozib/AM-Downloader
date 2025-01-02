// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;
using System.Windows;
using System.Windows.Interop;

namespace AMDownloader.Helpers
{
    public static class WindowUtils
    {
        public static IntPtr GetWindowHandle(Window window)
        {
            return new WindowInteropHelper(window).Handle;
        }

        public static void RemoveIcon(Window window)
        {
            var hwnd = GetWindowHandle(window);
            var extendedStyle = Native.User32.GetWindowLong(hwnd, Native.User32.GWL_EXSTYLE);

            Native.User32.SetWindowLong(hwnd, Native.User32.GWL_EXSTYLE, extendedStyle | Native.User32.WS_EX_DLGMODALFRAME);
            Native.User32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, Native.User32.SWP_NOMOVE
                | Native.User32.SWP_NOSIZE
                | Native.User32.SWP_NOZORDER
                | Native.User32.SWP_FRAMECHANGED);
            Native.User32.SendMessage(hwnd, Native.User32.WM_SETICON, new IntPtr(1), IntPtr.Zero);
            Native.User32.SendMessage(hwnd, Native.User32.WM_SETICON, IntPtr.Zero, IntPtr.Zero);
        }

        public static void RemoveMaxMinBox(Window window)
        {
            var hwnd = GetWindowHandle(window);
            var extendedStyle = Native.User32.GetWindowLong(hwnd, Native.User32.GWL_STYLE);

            extendedStyle &= 0xFFFFFFFF ^ (Native.User32.WS_MINIMIZEBOX | Native.User32.WS_MAXIMIZEBOX);
            Native.User32.SetWindowLong(hwnd, Native.User32.GWL_STYLE, extendedStyle);
        }
    }
}
