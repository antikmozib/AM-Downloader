using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using static AMDownloader.Common;

namespace AMDownloader
{
    class DownloaderObjectModel : INotifyPropertyChanged
    {
        private CancellationTokenSource _ctsPaused, _ctsCanceled;
        private CancellationToken _ctPause, _ctCancel;
        private TaskCompletionSource<DownloadStatus> _tcsDownloadItem;
        private IProgress<int> _progressReporter;
        private QueueProcessor _queueProcessor;
        private HttpClient _httpClient;

        public event PropertyChangedEventHandler PropertyChanged;
        public enum DownloadStatus
        {
            Ready, Queued, Downloading, Paused, Pausing, Finished, Error, Cancelling, Connecting
        }

        #region Properties

        public bool IsQueued
        {
            get
            {
                return (_queueProcessor != null);
            }
        }
        public HttpClient Client
        {
            get
            {
                return _httpClient;
            }
        }
        public string Name { get; private set; }
        public string Url { get; private set; }
        public string Destination { get; private set; }
        public DownloadStatus Status { get; private set; }
        public int Progress { get; private set; }
        public long TotalBytesCompleted { get; private set; }
        public long? TotalBytesToDownload { get; private set; } // nullable
        public long BytesDownloadedThisSession { get; private set; }
        public bool SupportsResume { get; private set; }
        public long? Speed { get; private set; } // nullable
        public DateTime DateCreated { get; private set; }
        public string PrettySpeed
        {
            get
            {
                return PrettySpeed(this.Speed);
            }
        }
        public string PrettyTotalSize
        {
            get
            {
                return PrettyNum(this.TotalBytesToDownload);
            }
        }
        public string PrettyDownloadedSoFar
        {
            get
            {
                return PrettyNum(this.TotalBytesCompleted);
            }
        }
        public string PrettyDestination
        {
            get
            {
                return new FileInfo(this.Destination).Directory.Name + " (" + this.Destination.Substring(0, this.Destination.Length - this.Name.Length - 1) + ")";
            }
        }
        public string PrettyDateCreated
        {
            get
            {
                return this.DateCreated.ToString("dd MMM yy H:mm:ss");
            }
        }

        #endregion

        #region Constructors

        public DownloaderObjectModel(ref HttpClient httpClient, string url, string destination) : this(ref httpClient, url, destination, null)
        {
        }

        public DownloaderObjectModel(ref HttpClient httpClient, string url, string destination, QueueProcessor queueProcessor)
        {
            this._httpClient = httpClient;
            this._queueProcessor = queueProcessor;

            // capture sync context
            _progressReporter = new Progress<int>((value) =>
            {
                this.Progress = value;
                AnnouncePropertyChanged(nameof(this.Progress));
            });

            if (File.Exists(destination))
            {
                // The file we're trying to download must NOT exist...
                throw new IOException();
            }

            this.Name = Path.GetFileName(destination);
            this.Url = url;
            this.Destination = destination;
            this.Status = DownloadStatus.Ready;
            this.Progress = 0;
            this.TotalBytesCompleted = 0;
            this.TotalBytesToDownload = null;
            this.BytesDownloadedThisSession = 0;
            this.Speed = null;
            this.SupportsResume = false;
            this.DateCreated = DateTime.Now;
            if (queueProcessor != null)
            {
                this.Status = DownloadStatus.Queued;
            }

            Task.Run(async () => await DetermineTotalBytesToDownloadAsync()).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    this.TotalBytesToDownload = null;
                    this.Status = DownloadStatus.Error;
                    this.Dequeue();
                }
                AnnouncePropertyChanged(nameof(this.Status));
            });
        }

        #endregion

        #region Private methods

        private async Task DetermineTotalBytesToDownloadAsync()
        {
            if (!await this.IsValidUrlAsync())
            {
                throw new Exception();
            }

            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, this.Url);
                using (HttpResponseMessage response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? null;
                    if (this.TotalBytesToDownload != null)
                    {
                        this.SupportsResume = true;
                    }
                    AnnouncePropertyChanged(nameof(this.TotalBytesToDownload));
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

            _ctsPaused = new CancellationTokenSource();
            _ctsCanceled = new CancellationTokenSource();

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

            _ctPause = _ctsPaused.Token;
            _ctCancel = _ctsCanceled.Token;
            linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_ctPause, _ctCancel).Token;

            using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
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
                        if (_ctPause.IsCancellationRequested)
                        {
                            // Paused...
                            _status = DownloadStatus.Paused;
                        }
                        if (_ctCancel.IsCancellationRequested)
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
                        _status = DownloadStatus.Finished;
                        progressReporter.Report(100);

                        if (this.TotalBytesToDownload == null)
                        {
                            // For downloads without total size, update total size once completed
                            this.TotalBytesToDownload = this.TotalBytesCompleted;
                        }
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

            _ctsCanceled = null;
            _ctsPaused = null;
            _ctCancel = default;
            _ctPause = default;
            _tcsDownloadItem.SetResult(_status);
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
                using (HttpResponseMessage response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
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

        public void Dequeue()
        {
            if (_queueProcessor != null)
            {
                _queueProcessor = null;
                AnnouncePropertyChanged(nameof(this.IsQueued));
            }
        }

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
                this.Name = Path.GetFileName(destination);
                AnnouncePropertyChanged(nameof(this.Destination));
                AnnouncePropertyChanged(nameof(this.Name));
                return true;
            }
        }

        public async Task StartAsync()
        {
            long bytesAlreadyDownloaded = 0;

            if (_ctsPaused != null)
            {
                // Download in progress
                return;
            }
            else if (_ctsCanceled != null || (_ctsPaused == null && _ctsCanceled == null))
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
                        this.Dequeue();
                        return;
                    }

                    if (File.Exists(this.Destination))
                    {
                        // Download is paused; resume from specific point
                        bytesAlreadyDownloaded = new FileInfo(this.Destination).Length;
                    }
                    else if (_queueProcessor != null && _queueProcessor.Contains(this))
                    {
                        // This is a part of the queue; start queue instead
                        await _queueProcessor.StartAsync(this);
                        return;
                    }
                }
            }

            _tcsDownloadItem = new TaskCompletionSource<DownloadStatus>(DownloadAsync(_progressReporter, bytesAlreadyDownloaded));

            this.BytesDownloadedThisSession = 0;
            this.Status = DownloadStatus.Downloading;
            AnnouncePropertyChanged(nameof(this.Status));

            await _tcsDownloadItem.Task;
            switch (_tcsDownloadItem.Task.Result)
            {
                case (DownloadStatus.Ready): // a.k.a canceled
                    this.Cancel();
                    break;

                case (DownloadStatus.Paused):
                    this.Pause();
                    break;

                case (DownloadStatus.Finished):
                    this.Status = DownloadStatus.Finished;
                    AnnouncePropertyChanged(nameof(this.Status));
                    this.Dequeue();
                    break;

                default:
                    this.Cancel();
                    break;
            }
        }

        public void Pause()
        {
            if (IsDownloadComplete() || _tcsDownloadItem == null)
            {
                // Nothing to pause
                return;
            }

            if (_ctsPaused != null)
            {
                // Download in progress; request cancellation
                _ctsPaused.Cancel();

                this.Status = DownloadStatus.Pausing;
                AnnouncePropertyChanged(nameof(this.Status));
            }
            else if (_tcsDownloadItem.Task.Result == DownloadStatus.Paused)
            {
                // Download paused; update status
                this.Status = DownloadStatus.Paused;
                AnnouncePropertyChanged(nameof(this.Status));
            }
        }

        public void Cancel()
        {
            if (_tcsDownloadItem == null)
            {
                // Nothing to cancel
                return;
            }

            if (_ctsCanceled != null)
            {
                // Download in progress; request cancellation
                _ctsCanceled.Cancel();

                this.Status = DownloadStatus.Cancelling;
                AnnouncePropertyChanged(nameof(this.Status));
            }
            else if (!IsDownloadComplete())
            {
                // Cancellation complete; delete partially downloaded file
                Task.Run(async () =>
                {
                    int numRetries = 0;

                    while (File.Exists(this.Destination) && numRetries++ < 30)
                    {
                        // if deletion fails, retry 30 times with 1 sec interval
                        try
                        {
                            File.Delete(this.Destination);
                            _progressReporter.Report(0);
                            this.TotalBytesCompleted = 0;
                            AnnouncePropertyChanged(nameof(this.TotalBytesCompleted));
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
                    AnnouncePropertyChanged(nameof(this.Status));
                    if (_queueProcessor != null)
                    {
                        // If was in queue, stop queue and dequeue this item
                        _queueProcessor.Stop();
                        this.Dequeue();
                    }
                });
            }
        }

        #endregion
    }
}
