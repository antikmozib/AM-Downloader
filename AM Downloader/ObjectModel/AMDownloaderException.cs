// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

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