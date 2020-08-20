using System;
using System.Net;

namespace AMDownloader.ObjectModel
{
    struct RequestModel
    {
        public string Url;
        public DateTime SeenAt;
        public long? TotalBytesToDownload;
        public HttpStatusCode? Status;
    }
}