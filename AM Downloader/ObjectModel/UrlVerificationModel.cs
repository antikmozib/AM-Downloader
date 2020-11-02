// Copyright (C) 2020 Antik Mozib. Released under CC BY-NC-SA 4.0.

using System.Net;

namespace AMDownloader.ObjectModel
{
    internal struct UrlVerificationModel
    {
        public HttpStatusCode? StatusCode;
        public long? TotalBytesToDownload;
    }
}