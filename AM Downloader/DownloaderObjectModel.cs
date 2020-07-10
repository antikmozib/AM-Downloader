using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using static AMDownloader.Shared;

namespace AMDownloader
{
    class DownloaderObjectModel : INotifyPropertyChanged
    {
        private CancellationTokenSource ctsPaused, ctsCanceled;
        private CancellationToken ctPause, ctCancel;
        private TaskCompletionSource<DownloadStatus> tcsDownloadItem;
        private IProgress<int> reporterProgress;

        public event PropertyChangedEventHandler PropertyChanged;
        public enum DownloadStatus
        {
            Ready, Queued, Downloading, Paused, Pausing, Complete, Error, Cancelling, Connecting
        }

        #region Properties
        public HttpClient Client { get; private set; }
        public string Filename { get; private set; }
        public string Url { get; private set; }
        public string Destination { get; private set; }
        public DownloadStatus Status { get; private set; }
        public int Progress { get; private set; }
        public long TotalBytesCompleted { get; private set; }
        public long? TotalBytesToDownload { get; private set; } // nullable
        public long BytesDownloadedThisSession { get; private set; }
        public bool SupportsResume { get; private set; }
        public long? Speed { get; private set; } // nullable
        public string PrettySpeed
        {
            get
            {
                if (this.Speed > 1024)
                {
                    return ((double)this.Speed / (double)1024).ToString("#0.00") + " MB/s";
                }
                else
                {
                    if (this.Speed == null)
                    {
                        return string.Empty;
                    }
                    else
                    {
                        return this.Speed.ToString() + " KB/s";
                    }
                }
            }
        }
        public string PrettyTotalSize
        {
            get
            {
                return PrettyNum<long?>(this.TotalBytesToDownload);
            }
        }
        public string PrettyDownloadedSoFar
        {
            get
            {
                return PrettyNum<long>(this.TotalBytesCompleted);
            }
        }
        #endregion

        public DownloaderObjectModel(ref HttpClient client, string url, string destination, bool start = false, bool addToQueue = false)
        {
            // capture sync context
            reporterProgress = new Progress<int>((value) =>
            {
                this.Progress = value;
                AnnouncePropertyChanged("Progress");
            });

            if (File.Exists(destination))
            {
                // The file we're trying to download must NOT exist...
                throw new IOException();
            }

            this.Filename = Path.GetFileName(destination);
            this.Url = url;
            this.Destination = destination;
            this.Status = DownloadStatus.Ready;
            this.Progress = 0;
            this.TotalBytesCompleted = 0;
            this.TotalBytesToDownload = null;
            this.BytesDownloadedThisSession = 0;
            this.Speed = null;
            this.SupportsResume = false;
            this.Client = client;

            Task.Run(async () => await DetermineTotalBytesToDownload()).ContinueWith(t =>
            {
                if (t.Exception == null)
                {
                    if (addToQueue)
                    {
                        this.Status = DownloadStatus.Queued;
                    }
                    else
                    {
                        if (start)
                        {
                            Task.Run(async () => await this.StartAsync());
                        }
                    }
                }
                else
                {
                    this.TotalBytesToDownload = null;
                    this.Status = DownloadStatus.Error;

                }
                AnnouncePropertyChanged(nameof(this.Status));
            });
        }

        #region Private methods

        private async Task DetermineTotalBytesToDownload()
        {
            // Determine total size to download ASYNC!
            if (!await this.IsValidUrlAsync())
            {
                throw new Exception();
            }

            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, this.Url);
                using (HttpResponseMessage response = await this.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? null;
                    if (this.TotalBytesToDownload != null)
                    {
                        this.SupportsResume = true;
                    }
                    AnnouncePropertyChanged("TotalBytesToDownload");
                    AnnouncePropertyChanged(nameof(this.PrettyTotalSize));
                }
            }
            catch
            {
                throw new Exception();
            }

        }

        private async Task DownloadAsync(IProgress<int> progressReporter, long bytesDownloadedPreviously = 0)
        {
            CancellationToken linkedTokenSource;
            DownloadStatus _status = DownloadStatus.Error;
            HttpRequestMessage request;

            ctsPaused = new CancellationTokenSource();
            ctsCanceled = new CancellationTokenSource();

            if (this.SupportsResume)
            {
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get,
                    Headers = { Range = new RangeHeaderValue(bytesDownloadedPreviously, this.TotalBytesToDownload) }
                };
            }
            else
            {
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get
                };
            }

            ctPause = ctsPaused.Token;
            ctCancel = ctsCanceled.Token;
            linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ctPause, ctCancel).Token;

            using (HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
            using (FileStream destinationStream = new FileStream(this.Destination, FileMode.Append))
            using (BinaryWriter binaryWriter = new BinaryWriter(destinationStream))
            {
                request.Dispose();
                byte[] buffer = new byte[1024];
                bool moreToRead = true;
                long nextProgressUpdate;
                long progressUpdateFrequency; // num bytes after which to report progress

                this.Speed = new long();

                if (this.TotalBytesToDownload != null)
                {
                    // report progress every 1%
                    progressUpdateFrequency = (long)this.TotalBytesToDownload / 100;
                }
                else
                {
                    // report progress every 1 byte
                    progressUpdateFrequency = 1;
                }

                nextProgressUpdate = progressUpdateFrequency;

                StartMeasuringSpeed();

                while (moreToRead == true)
                {
                    if (linkedTokenSource.IsCancellationRequested)
                    {
                        if (ctPause.IsCancellationRequested)
                        {
                            // Paused...
                            _status = DownloadStatus.Paused;
                        }
                        if (ctCancel.IsCancellationRequested)
                        {
                            // Cancelled...
                            _status = DownloadStatus.Ready;
                        }
                        break;
                    }

                    int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);

                    if (read == 0)
                    {
                        moreToRead = false;
                        _status = DownloadStatus.Complete;
                        progressReporter.Report(100);
                    }
                    else
                    {
                        byte[] data = new byte[read];
                        buffer.ToList().CopyTo(0, data, 0, read);

                        binaryWriter.Write(data, 0, data.Length);

                        this.BytesDownloadedThisSession += read;
                        this.TotalBytesCompleted = bytesDownloadedPreviously + this.BytesDownloadedThisSession;

                        if (this.TotalBytesCompleted >= nextProgressUpdate)
                        {
                            if (this.TotalBytesToDownload != null)
                            {
                                progressReporter.Report((int)
                                    ((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100));

                                if (nextProgressUpdate >= this.TotalBytesToDownload - progressUpdateFrequency)
                                {
                                    progressUpdateFrequency = (long)this.TotalBytesToDownload - this.TotalBytesCompleted;
                                }
                            }

                            nextProgressUpdate = this.TotalBytesCompleted + progressUpdateFrequency;
                            AnnouncePropertyChanged(nameof(this.BytesDownloadedThisSession));
                            AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                            AnnouncePropertyChanged(nameof(this.TotalBytesCompleted));
                        }
                    }
                }
            }

            ctsCanceled = null;
            ctsPaused = null;
            tcsDownloadItem.SetResult(_status);
        }

        private void StartMeasuringSpeed()
        {
            Stopwatch sw = new Stopwatch();

            sw.Start();

            Task.Run(async () =>
            {
                while (this.Status == DownloadStatus.Downloading)
                {
                    if (sw.ElapsedMilliseconds >= 1000 && this.BytesDownloadedThisSession > 0)
                    {
                        double speed = (this.BytesDownloadedThisSession / 1024) / (sw.ElapsedMilliseconds / 1000);
                        this.Speed = (long)speed;
                        AnnouncePropertyChanged(nameof(this.Speed));
                        AnnouncePropertyChanged(nameof(this.PrettySpeed));
                    }
                    await Task.Delay(1000);

                }
            }).ContinueWith((cw) =>
            {
                sw.Stop();
                this.Speed = null;
                AnnouncePropertyChanged(nameof(this.Speed));
                AnnouncePropertyChanged(nameof(this.PrettySpeed));
            });
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private bool IsDownloadComplete()
        {
            return (this.TotalBytesToDownload != null &&
                        this.TotalBytesToDownload > 0 &&
                        File.Exists(this.Destination) &&
                        new FileInfo(this.Destination).Length >= this.TotalBytesToDownload);
        }

        private async Task<bool> IsValidUrlAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, this.Url);
                using (HttpResponseMessage response = await this.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Public methods

        public bool TrySetDestination(string destination)
        {
            if (File.Exists(destination))
            {
                throw new IOException();
            }

            if (this.Status != DownloadStatus.Ready)
            {
                return false;
            }
            else
            {
                this.Destination = destination;
                this.Filename = Path.GetFileName(destination);
                AnnouncePropertyChanged(nameof(this.Destination));
                AnnouncePropertyChanged(nameof(this.Filename));
                return true;
            }
        }

        public async Task StartAsync()
        {
            long bytesAlreadyDownloaded = 0;

            if (ctsPaused != null)
            {
                // Download in progress
                return;
            }
            else if (ctsCanceled != null || (ctsPaused == null && ctsCanceled == null))
            {
                if (IsDownloadComplete())
                {
                    // Don't start an already completed download
                    return;
                }
                else
                {
                    this.Status = DownloadStatus.Connecting;
                    AnnouncePropertyChanged(nameof(this.Status));

                    // Ensure url is valid for all downloads
                    if (!await IsValidUrlAsync())
                    {
                        this.Status = DownloadStatus.Error;
                        AnnouncePropertyChanged(nameof(this.Status));
                        return;
                    }

                    if (File.Exists(this.Destination))
                    {
                        // Download is paused; resume from specific point
                        bytesAlreadyDownloaded = new FileInfo(this.Destination).Length;
                    }
                }
            }

            tcsDownloadItem = new TaskCompletionSource<DownloadStatus>(DownloadAsync(reporterProgress, bytesAlreadyDownloaded));

            this.BytesDownloadedThisSession = 0;
            this.Status = DownloadStatus.Downloading;
            AnnouncePropertyChanged("Status");

            await tcsDownloadItem.Task;
            switch (tcsDownloadItem.Task.Result)
            {
                case (DownloadStatus.Ready): // a.k.a canceled
                    this.Cancel();
                    break;

                case (DownloadStatus.Paused):
                    this.Pause();
                    break;

                case (DownloadStatus.Complete):
                    this.Status = DownloadStatus.Complete;
                    AnnouncePropertyChanged("Status");
                    break;

                default:
                    this.Cancel();
                    break;
            }
        }

        public void Pause()
        {
            if (IsDownloadComplete() || tcsDownloadItem == null)
            {
                // Nothing to pause
                return;
            }

            if (ctsPaused != null)
            {
                // Download in progress; request cancellation
                ctsPaused.Cancel();

                this.Status = DownloadStatus.Pausing;
                AnnouncePropertyChanged(nameof(this.Status));
            }
            else if (tcsDownloadItem.Task.Result == DownloadStatus.Paused)
            {
                // Download paused; update status
                this.Status = DownloadStatus.Paused;
                AnnouncePropertyChanged("Status");
            }
        }

        public void Cancel()
        {
            if (tcsDownloadItem == null)
            {
                // Nothing to cancel
                return;
            }

            if (ctsCanceled != null)
            {
                // Download in progress; request cancellation
                ctsCanceled.Cancel();

                this.Status = DownloadStatus.Cancelling;
                AnnouncePropertyChanged(nameof(this.Status));
            }
            else if (!IsDownloadComplete())
            {
                // Delete partially downloaded file
                Task.Run(async () =>
                {
                    int numRetries = 0;

                    while (File.Exists(this.Destination) && numRetries++ < 30)
                    {
                        // if deletion fails, retry every 1 sec for 30 secs
                        try
                        {
                            File.Delete(this.Destination);
                            reporterProgress.Report(0);
                            this.TotalBytesCompleted = 0;
                            AnnouncePropertyChanged("TotalBytesCompleted");
                            AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                        }
                        catch (IOException)
                        {
                            // file used by other apps; retry after 1 sec
                            await Task.Delay(1000);
                            continue;
                        }
                        catch (Exception)
                        {
                            break;
                        }
                    }
                }).ContinueWith(t =>
                {
                    this.Status = DownloadStatus.Ready;
                    AnnouncePropertyChanged("Status");
                });
            }
        }

        #endregion
    }
}
