﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Threading;
using System.Windows;

namespace AMDownloader.ClipboardObservation
{
    internal class ClipboardObserver
    {
        public void SetText(string value)
        {
            Thread t = new Thread(() =>
            {
                Clipboard.SetText(value);
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }

        public string GetText()
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

        public void Clear()
        {
            Thread t = new Thread(() =>
            {
                Clipboard.Clear();
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }
    }
}