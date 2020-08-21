using System;
using System.Net;

namespace AMDownloader.RequestThrottling.Model
{
    struct RequestModel
    {
        public string Url;
        public DateTime SeenAt;
        public long? TotalBytesToDownload;
        public HttpStatusCode? StatusCode;
    }
}