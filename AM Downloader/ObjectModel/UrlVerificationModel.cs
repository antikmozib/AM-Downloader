// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Net;

namespace AMDownloader.ObjectModel
{
    internal struct UrlVerificationModel
    {
        public HttpStatusCode? StatusCode;
        public long? TotalBytesToDownload;
    }
}