// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System.Net;

namespace AMDownloader.ObjectModel
{
    internal struct UrlVerificationModel
    {
        public HttpStatusCode? StatusCode;
        public long? TotalBytesToDownload;
    }
}