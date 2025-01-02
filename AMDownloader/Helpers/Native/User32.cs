// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace AMDownloader.Helpers.Native
{
    public static class User32
    {
        public const uint WS_EX_CONTEXTHELP = 0x00000400;
        public const uint WS_MINIMIZEBOX = 0x00020000;
        public const uint WS_MAXIMIZEBOX = 0x00010000;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_FRAMECHANGED = 0x0020;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int SC_CONTEXTHELP = 0xF180;
        public const int WM_SETICON = 0x0080;
        public const int WS_EX_DLGMODALFRAME = 0x0001;

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, uint newStyle);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        public static extern int DestroyIcon(IntPtr hIcon);
    }
}
