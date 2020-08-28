using System.Net;

namespace AMDownloader.ObjectModel
{
    struct UrlVerificationModel
    {
        public HttpStatusCode? StatusCode;
        public long? TotalBytesToDownload;
    }
}