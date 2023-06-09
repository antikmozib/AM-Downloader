// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;

namespace AMDownloader.ObjectModel
{
    /// <summary>
    /// Represents errors related to the processing of downloads.
    /// </summary>
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

    /// <summary>
    /// The exception that is thrown if an URL returns an invalid status.
    /// </summary>
    internal class AMDownloaderUrlException : AMDownloaderException
    {
        public AMDownloaderUrlException()
        {
        }

        public AMDownloaderUrlException(string message)
            : base(message)
        {
        }

        public AMDownloaderUrlException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}