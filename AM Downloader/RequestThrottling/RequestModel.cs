// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

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