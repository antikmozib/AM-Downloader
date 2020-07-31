using System;
using System.Collections.Generic;
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
    public delegate void RefreshCollectionDelegate();

    class DownloaderObjectModel : INotifyPropertyChanged, IQueueable
    {
        #region Fields

        private const int DEFAULT_NUM_STREAMS = 1;
        private CancellationTokenSource _ctsPaused, _ctsCanceled;
        private CancellationToken _ctPause, _ctCancel;
        private readonly IProgress<int> _progressReporter;
        private HttpClient _httpClient;
        private TaskCompletionSource<DownloadStatus> _taskCompletion;
        private RefreshCollectionDelegate _refreshCollectionDel;

        #endregion // Fields

        #region Properties

        public event PropertyChangedEventHandler PropertyChanged;
        public enum DownloadStatus
        {
            Ready, Queued, Downloading, Paused, Pausing, Finished, Error, Cancelling, Connecting, Merging
        }
        public bool IsQueued { get; private set; }
        public bool IsBeingDownloaded { get { return (_ctsPaused != null); } }
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
        public string PrettySpeed { get { return PrettySpeed(this.Speed); } }
        public string PrettyTotalSize { get { return PrettyNum(this.TotalBytesToDownload); } }
        public string PrettyDownloadedSoFar { get { return PrettyNum(this.TotalBytesCompleted); } }
        public string PrettyDestination
        {
            get { return new FileInfo(this.Destination).Directory.Name + " (" + this.Destination.Substring(0, this.Destination.Length - this.Name.Length - 1) + ")"; }
        }
        public string PrettyDateCreated { get { return this.DateCreated.ToString("dd MMM yy H:mm:ss"); } }
        public int? NumberOfActiveStreams { get; private set; } // nullable

        #endregion // Properties

        #region Constructors

        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            PropertyChangedEventHandler propertyChangedEventHandler,
            RefreshCollectionDelegate refreshCollectionDelegate)
        {
            _httpClient = httpClient;
            PropertyChanged += propertyChangedEventHandler;
            _refreshCollectionDel = refreshCollectionDelegate;

            // capture sync context
            _progressReporter = new Progress<int>((value) =>
            {
                this.Progress = value;
                AnnouncePropertyChanged(nameof(this.Progress));
            });

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
            this.NumberOfActiveStreams = null;
            if (enqueue)
            {
                this.Enqueue();
            }

            Task.Run(async () => await DetermineTotalBytesToDownloadAsync()).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    // invalid url
                    this.Dequeue();
                    this.TotalBytesToDownload = null;
                    this.Status = DownloadStatus.Error;
                }
                AnnouncePropertyChanged(nameof(this.Status));
            }).ContinueWith(t =>
            {
                if (File.Exists(this.Destination) && this.Status != DownloadStatus.Error)
                {
                    this.TotalBytesCompleted = new FileInfo(this.Destination).Length;
                    AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));

                    if (IsDownloadComplete())
                    {
                        if (this.IsQueued) this.Dequeue();

                        this.Progress = 100;
                        this.Status = DownloadStatus.Finished;
                    }
                    else if (this.SupportsResume)
                    {
                        this.Progress = (int)((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100);
                        this.Status = DownloadStatus.Paused;
                    }
                    AnnouncePropertyChanged(nameof(this.Progress));
                    AnnouncePropertyChanged(nameof(this.Status));
                }
            });
        }

        #endregion // Constructors

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
                    if (this.TotalBytesToDownload != null && this.TotalBytesToDownload > 0)
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

        private async Task MergeFilesAsync(params FileInfo[] files)
        {
            var destStream = new FileStream(this.Destination, FileMode.Append, FileAccess.Write);
            var binaryWriter = new BinaryWriter(destStream);

            foreach (var file in files)
            {
                if (!file.Exists) continue;

                var sourceStream = new FileStream(file.FullName, FileMode.Open);
                byte[] buffer = new byte[1024];

                while (true)
                {
                    int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        byte[] data = new byte[read];
                        buffer.ToList().CopyTo(0, data, 0, read);
                        binaryWriter.Write(data, 0, data.Length);
                    }
                    else
                    {
                        break;
                    }
                }
                sourceStream.Close();
                file.Delete();
            }

            binaryWriter.Close();
            destStream.Close();
        }

        private async Task<DownloadStatus> ProcessStreamsAsync(int numStreams, long bytesDownloadedPreviously = 0)
        {
            var status = DownloadStatus.Error;
            List<HttpRequestMessage> requests = new List<HttpRequestMessage>();
            List<Task> tasks = new List<Task>();
            long progressReportingFrequency = 1024;
            long nextProgressReportAt=1024;

            IProgress<long> streamProgress = new Progress<long>((value) =>
            {
                this.BytesDownloadedThisSession += value;
                this.TotalBytesCompleted = bytesDownloadedPreviously + this.BytesDownloadedThisSession;

                if (this.SupportsResume && this.TotalBytesCompleted >= nextProgressReportAt)
                {
                    double progress = (double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100;
                    _progressReporter.Report((int)progress);
                    AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                    nextProgressReportAt += progressReportingFrequency;
                    if (nextProgressReportAt > this.TotalBytesToDownload) nextProgressReportAt = this.TotalBytesToDownload ?? 0;
                }
            });

            long pointFrequency = ((this.TotalBytesToDownload ?? 0) - bytesDownloadedPreviously) / numStreams;

            _ctsPaused = new CancellationTokenSource();
            _ctsCanceled = new CancellationTokenSource();
            _ctPause = _ctsPaused.Token;
            _ctCancel = _ctsCanceled.Token;
            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_ctPause, _ctCancel).Token;

            if (!this.SupportsResume)
            {
                // cannot support multiple streams
                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get
                };
                requests.Add(request);
            }
            else
            {
                progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 100;
                nextProgressReportAt = progressReportingFrequency;

                // Set up the requests
                long startPos = 0;

                if (bytesDownloadedPreviously > 0)
                {
                    startPos = bytesDownloadedPreviously;
                }

                for (int i = 0; i < numStreams; i++)
                {
                    long fromPos = startPos + pointFrequency * i;
                    long toPos;

                    if (i == 0 && startPos == 0) fromPos = 0;

                    if (i == numStreams - 1)
                    {
                        toPos = this.TotalBytesToDownload ?? 0;
                    }
                    else
                    {
                        toPos = fromPos + pointFrequency - 1;
                    }

                    var request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get,
                        Headers = { Range = new RangeHeaderValue(fromPos, toPos) }
                    };

                    requests.Add(request);
                }
            }

            this.NumberOfActiveStreams = 0;
            AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Set up the tasks to process the requests
            foreach (var request in requests)
            {
                Task t = Task.Run(async () =>
                {
                    this.NumberOfActiveStreams += 1;
                    AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));

                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    var sourceStream = await response.Content.ReadAsStreamAsync();
                    var destinationStream = new FileStream(this.Destination + ".part" + requests.IndexOf(request), FileMode.Append);
                    var binaryWriter = new BinaryWriter(destinationStream);

                    byte[] buffer = new byte[1024];

                    while (true)
                    {
                        if (linkedToken.IsCancellationRequested)
                            break;

                        int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);

                        if (read == 0)
                        {
                            // Stream complete
                            break;
                        }
                        else
                        {
                            byte[] data = new byte[read];
                            buffer.ToList().CopyTo(0, data, 0, read);

                            binaryWriter.Write(data, 0, data.Length);
                            streamProgress.Report(read);
                        }
                    }

                    binaryWriter.Close();
                    destinationStream.Close();
                    sourceStream.Close();
                    response.Dispose();
                    request.Dispose();                    

                    this.NumberOfActiveStreams -= 1;
                    AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));
                }, linkedToken);

                tasks.Add(t);
            }

            // Run the tasks
            StartMeasuringSpeed();
            await Task.WhenAll(tasks);

            this.NumberOfActiveStreams = null;
            AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Merge the streams
            this.Status = DownloadStatus.Merging;
            AnnouncePropertyChanged(nameof(this.Status));            
            FileInfo[] files = new FileInfo[numStreams];
            for (int i = 0; i < numStreams; i++)
            {
                files[i] = new FileInfo(this.Destination + ".part" + i);
            }
            await MergeFilesAsync(files);

            if (linkedToken.IsCancellationRequested)
            {
                if (_ctPause.IsCancellationRequested)
                {
                    status = DownloadStatus.Paused;
                }
                else if (_ctCancel.IsCancellationRequested)
                {
                    status = DownloadStatus.Ready;
                }
            }
            else
            {
                if (new FileInfo(this.Destination).Length >= this.TotalBytesToDownload)
                {
                    status = DownloadStatus.Finished;
                }
            }

            _ctsCanceled = null;
            _ctsPaused = null;
            _ctCancel = default;
            _ctPause = default;

            _taskCompletion.SetResult(status);
            return status;
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
            }).ContinueWith((t) =>
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

        #endregion // Private methods

        #region Public methods

        public void Enqueue()
        {
            if (this.IsQueued || this.Status != DownloadStatus.Ready) return;

            this.IsQueued = true;
            this.Status = DownloadStatus.Queued;
            AnnouncePropertyChanged(nameof(this.Status));
            AnnouncePropertyChanged(nameof(this.IsQueued));
        }

        public void Dequeue()
        {
            if (this.IsQueued && !this.IsBeingDownloaded)
            {
                this.IsQueued = false;
                AnnouncePropertyChanged(nameof(this.IsQueued));

                if (this.Status == DownloadStatus.Queued)
                {
                    this.Status = DownloadStatus.Ready;
                    AnnouncePropertyChanged(nameof(this.Status));
                }
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

        public async Task StartAsync(int numStreams = DEFAULT_NUM_STREAMS)
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
                }
            }

            _taskCompletion = new TaskCompletionSource<DownloadStatus>(ProcessStreamsAsync(numStreams, bytesAlreadyDownloaded));

            this.BytesDownloadedThisSession = 0;
            this.TotalBytesCompleted = bytesAlreadyDownloaded;
            this.Status = DownloadStatus.Downloading;
            AnnouncePropertyChanged(nameof(this.Status));
            AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));

            await _taskCompletion.Task;

            switch (_taskCompletion.Task.Result)
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

                    var fileInfo = new FileInfo(this.Destination);
                    if (this.TotalBytesCompleted < fileInfo.Length)
                    {
                        this.TotalBytesCompleted = fileInfo.Length;
                        AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                    }

                    if (this.Progress < 100)
                    {
                        _progressReporter.Report(100);
                    }
                    break;

                default:
                    this.Cancel();
                    break;
            }

            if (_refreshCollectionDel != null) _refreshCollectionDel.Invoke();
        }

        public void Pause()
        {
            if (IsDownloadComplete() || _taskCompletion == null)
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
            else if (_taskCompletion.Task.Result == DownloadStatus.Paused)
            {
                // Download paused; update status
                this.Status = DownloadStatus.Paused;
                AnnouncePropertyChanged(nameof(this.Status));
            }
        }

        public void Cancel()
        {
            if (_taskCompletion == null)
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
                    if (this.IsQueued)
                    {
                        // If was in queue, stop queue and dequeue this item
                        this.Dequeue();
                    }
                });
            }
        }

        public void SetCreationTime(DateTime newDateCreated)
        {
            this.DateCreated = newDateCreated;
            AnnouncePropertyChanged(nameof(this.DateCreated));
        }

        #endregion // Public methods
    }
}
