// Copyright (C) 2020-2021 Antik Mozib.

using System;

namespace AMDownloader.ObjectModel
{
    internal class AMDownloaderException : Exception
    {
        public AMDownloaderException()
        {
        }

        public AMDownloaderException(string message)
            : base(message)
        {
        }

        public AMDownloaderException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}