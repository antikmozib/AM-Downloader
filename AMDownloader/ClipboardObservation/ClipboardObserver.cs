// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AMDownloader.ClipboardObservation
{
    public static class ClipboardObserver
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
            string value = string.Empty;

            Thread t = new(() =>
            {
                try
                {
                    value = Clipboard.GetText();
                }
                catch (COMException)
                {

                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            return value;
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