using System.Net;

namespace AMDownloader.ObjectModel
{
    struct DownloadVerificationModel
    {
        public HttpStatusCode? Status;
        public long? TotalBytesToDownload;
    }
}