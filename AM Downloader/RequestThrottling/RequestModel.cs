// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System;
using System.Net;

namespace AMDownloader.RequestThrottling.Model
{
    internal struct RequestModel
    {
        public string Url;
        public DateTime SeenAt;
        public long? TotalBytesToDownload;
        public HttpStatusCode? StatusCode;
    }
}