// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Threading;
using System.Windows;

namespace AMDownloader.ClipboardObservation
{
    internal static class ClipboardObserver
    {
        public static void SetText(string value)
        {
            Thread t = new(() =>
            {
                Clipboard.SetText(value);
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }

        public static string GetText()
        {
            string val = string.Empty;

            Thread t = new Thread(() =>
            {
                val = Clipboard.GetText();
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            return val;
        }

        public static void Clear()
        {
            Thread t = new(() =>
            {
                Clipboard.Clear();
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }
    }
}