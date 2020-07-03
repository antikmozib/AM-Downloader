using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using System.Collections.ObjectModel;

namespace AM_Downloader
{
    class DownloaderModel
    {
        static readonly string downloadFolderPath = @"c:\users\antik\downloads\";

        public class DownloaderItemModel : INotifyPropertyChanged
        {
            private HttpClient _httpClient;
            private CancellationTokenSource ctsPause, ctsCancel;
            private CancellationToken ctPause, ctCancel;
            private Task downloadItemTask;

            public event PropertyChangedEventHandler PropertyChanged;
            public enum DownloadStatusType
            {
                ReadyToDownload, Queued, Downloading, Paused, Cancelled, Completed
            }

            #region Properties
            public string ShortFilename { get; private set; }
            public string Url { get; private set; }
            public string LongFilename { get; private set; }
            public DownloadStatusType Status { get; private set; }
            public int Progress { get; private set; }
            public long BytesDownloadedSoFar { get; private set; }
            public long TotalBytesToDownload { get; private set; }
            public bool SupportsResume { get; private set; }
            #endregion

            public DownloaderItemModel(ref HttpClient httpClient, String url, bool? addToQueue = true, bool start = true)
            {
                _httpClient = httpClient;

                string fileName = Path.GetFileName(url);
                string LongFilename = downloadFolderPath + "\\" + fileName;

                // Get a unique filename
                if (File.Exists(LongFilename))
                {
                    int i = 0;
                    while (File.Exists(LongFilename))
                    {
                        LongFilename = downloadFolderPath + "\\"
                            + fileName.Substring(0, fileName.Length - Path.GetExtension(fileName).Length)
                            + " (" + ++i + ")" + Path.GetExtension(fileName);
                    };
                }

                this.ShortFilename = Path.GetFileName(LongFilename);
                this.Url = url;
                this.LongFilename = LongFilename;
                this.Status = DownloadStatusType.Queued;
                this.Progress = 0;
                this.BytesDownloadedSoFar = 0;
                this.TotalBytesToDownload = 0;
                this.SupportsResume = false;

                // Determine total size to download
                Task.Run(async () =>
                {
                    using (HttpResponseMessage response = await _httpClient.GetAsync(this.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? 0;
                        RaisePropertyChanged("TotalBytesToDownload");
                    }
                });

                if (start)
                    Start();
            }

            // HELLO WORLDs

            private async Task DownloadItemAsync(IProgress<int> progressReporter)
            {
                // We need to add range to our request
                _httpClient.DefaultRequestHeaders.Clear();
                HttpRequestMessage request;

                if (this.Status == DownloadStatusType.Paused)
                {
                    request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get,
                        Headers = { Range = new RangeHeaderValue(new FileInfo(this.LongFilename).Length, this.TotalBytesToDownload) }
                    };
                }
                else
                {
                    request = new HttpRequestMessage { RequestUri = new Uri(this.Url), Method = HttpMethod.Get };
                }

                this.Status = DownloadStatusType.Downloading;
                RaisePropertyChanged("Status");

                HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
                {
                    using (FileStream destinationStream = new FileStream(this.LongFilename, FileMode.Append))
                    {
                        using (BinaryWriter binaryWriter = new BinaryWriter(destinationStream))
                        {
                            long? totalSize = response.Content.Headers.ContentLength;
                            long totalRead = 0;
                            byte[] buffer = new byte[4096];
                            bool moreToRead = true;

                            while (moreToRead == true)
                            {
                                if (ctCancel.IsCancellationRequested || ctPause.IsCancellationRequested)
                                {
                                    response.Dispose();
                                    request.Dispose();
                                    return;
                                }

                                int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);

                                if (read == 0)
                                {
                                    moreToRead = false;
                                }
                                else
                                {
                                    byte[] data = new byte[read];
                                    buffer.ToList().CopyTo(0, data, 0, read);

                                    binaryWriter.Write(data, 0, data.Length);

                                    totalRead += read;

                                    // prevent divison by zero error
                                    if (this.TotalBytesToDownload > this.BytesDownloadedSoFar && this.TotalBytesToDownload != 0)
                                    {
                                        double progressPercent = (double)this.BytesDownloadedSoFar / (double)this.TotalBytesToDownload * 100;
                                        progressReporter.Report((int)progressPercent);
                                    }

                                    this.BytesDownloadedSoFar = new FileInfo(this.LongFilename).Length;
                                    RaisePropertyChanged("BytesDownloadedSoFar");
                                }
                            }
                        }

                        this.Progress = 100;
                        RaisePropertyChanged("Progress");
                        this.Status = DownloadStatusType.Completed;
                        RaisePropertyChanged("Status");

                        response.Dispose();
                        request.Dispose();
                    }
                }
            }

            public void Start()
            {
                if (File.Exists(this.LongFilename) && this.Status == DownloadStatusType.Completed)
                {
                    File.Delete(this.LongFilename);
                }

                ctsCancel = new CancellationTokenSource();
                ctCancel = ctsCancel.Token;
                ctsPause = new CancellationTokenSource();
                ctPause = ctsPause.Token;

                downloadItemTask = new Task(async () => await DownloadItemAsync(new Progress<int>((int value) =>
                {
                    this.Progress = value;
                    RaisePropertyChanged("Progress");
                })));

                downloadItemTask.Start();
            }

            public void Pause()
            {
                if (this.Status == DownloadStatusType.Downloading)
                {
                    ctsPause.Cancel();
                    downloadItemTask.GetAwaiter().GetResult();

                    this.Status = DownloadStatusType.Paused;
                    RaisePropertyChanged("Status");
                }
            }

            public async Task CancelAsync()
            {
                if (this.Status == DownloadStatusType.Downloading)
                {
                    ctsCancel.Cancel();
                    downloadItemTask.GetAwaiter().GetResult();
                    await Task.Delay(1000); // otherwise file deletion fails
                }

                File.Delete(this.LongFilename);

                this.Progress = 0;
                RaisePropertyChanged("Progress");

                this.BytesDownloadedSoFar = 0;
                RaisePropertyChanged("BytesDownloadedSoFar");

                this.Status = DownloadStatusType.Cancelled;
                RaisePropertyChanged("Status");
            }

            protected void RaisePropertyChanged(string prop)
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(prop));
                }
            }
        }

        public class AddDownloadItemModel
        {
            private ObservableCollection<DownloaderItemModel> _downloadItems;
            private HttpClient _httpClient;

            public string Url { private get; set; }
            public bool Start { private get; set; }
            public bool AddToQueue { private get; set; }

            public RelayCommand AddCommand { private get; set; }

            public AddDownloadItemModel(ref HttpClient httpClient, ref ObservableCollection<DownloaderItemModel> downloadItems)
            {
                _downloadItems = downloadItems;
                _httpClient = httpClient;
                AddCommand = new RelayCommand(add);
            }

            private void add(object item)
            {
                _downloadItems.Add(new DownloaderItemModel(ref _httpClient, this.Url, null, Start));
            }
        }
    }
}
