// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

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