// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

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