using System.Net;

namespace AMDownloader.ObjectModel
{
    struct DownloadVerificationModel
    {
        public HttpStatusCode? StatusCode;
        public long? TotalBytesToDownload;
    }
}