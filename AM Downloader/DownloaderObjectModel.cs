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
using AMDownloader.Properties;
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
        private readonly RefreshCollectionDelegate _refreshCollectionDel;
        private readonly SemaphoreSlim _semaphore;
        #endregion // Fields

        #region Events
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler DownloadStarted;
        public event EventHandler DownloadFinished;
        public event EventHandler ItemEnqueued;
        public event EventHandler ItemDequeued;
        protected virtual void OnEventOccurrence(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }
        #endregion // Events

        #region Properties
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
        public string PrettySpeed { get { return PrettifySpeed(this.Speed); } }
        public string PrettyTotalSize { get { return PrettifySize(this.TotalBytesToDownload); } }
        public string PrettyDownloadedSoFar { get { return PrettifySize(this.TotalBytesCompleted); } }
        public string PrettyDestination
        {
            get { return new FileInfo(this.Destination).Directory.Name + " (" + this.Destination.Substring(0, this.Destination.Length - this.Name.Length - 1) + ")"; }
        }
        public string PrettyDateCreated { get { return this.DateCreated.ToString("dd MMM yy H:mm:ss"); } }
        public int? NumberOfActiveStreams { get; private set; } // nullable
        public bool IsCompleted => IsDownloadComplete();
        #endregion // Properties

        #region Constructors
        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            EventHandler downloadStartedEventHandler,
            EventHandler downloadFinishedEventHandler,
            EventHandler itemEnqueuedEventHandler,
            EventHandler itemDequeuedEventHandler,
            PropertyChangedEventHandler propertyChangedEventHandler,
            RefreshCollectionDelegate refreshCollectionDelegate)
        {
            _httpClient = httpClient;
            PropertyChanged += propertyChangedEventHandler;
            DownloadStarted += downloadStartedEventHandler;
            DownloadFinished += downloadFinishedEventHandler;
            ItemEnqueued += itemEnqueuedEventHandler;
            ItemDequeued += itemDequeuedEventHandler;

            _refreshCollectionDel = refreshCollectionDelegate;
            _semaphore = new SemaphoreSlim(1);

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
            if (enqueue) this.Enqueue();

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

                    if (!this.IsBeingDownloaded)
                    {
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
                    else
                    {
                        this.SupportsResume = false;
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
            try
            {
                using (var destStream = new FileStream(this.Destination, FileMode.Append, FileAccess.Write))
                using (var binaryWriter = new BinaryWriter(destStream))
                {
                    foreach (var file in files)
                    {
                        using (var sourceStream = new FileStream(file.FullName, FileMode.Open))
                        {
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
                        }
                        file.Delete();
                    }
                }

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private async Task<DownloadStatus> ProcessStreamsAsync(int numStreams, long bytesDownloadedPreviously = 0)
        {
            const int bufferLength = (int)Common.KILOBYTE;
            var status = DownloadStatus.Error;
            List<HttpRequestMessage> requests = new List<HttpRequestMessage>();
            List<Task> tasks = new List<Task>();
            long progressReportingFrequency = bufferLength;
            long nextProgressReportAt = bufferLength;
            object _lock = new object();
            SemaphoreSlim semaphoreProgress = new SemaphoreSlim(1);

            IProgress<int> streamProgress = new Progress<int>(async (value) =>
            {
                await semaphoreProgress.WaitAsync();
                this.BytesDownloadedThisSession += value;
                this.TotalBytesCompleted = this.BytesDownloadedThisSession + bytesDownloadedPreviously;

                if (this.SupportsResume && (this.TotalBytesCompleted >= nextProgressReportAt || this.TotalBytesCompleted > this.TotalBytesToDownload - progressReportingFrequency))
                {
                    double progress = (double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100;
                    //_progressReporter.Report((int)progress);
                    this.Progress = (int)progress;
                    AnnouncePropertyChanged(nameof(this.Progress));
                    AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                    nextProgressReportAt += progressReportingFrequency;
                }
                semaphoreProgress.Release();
            });

            long chunkSize = ((this.TotalBytesToDownload ?? 0) - bytesDownloadedPreviously) / numStreams;

            _ctsPaused = new CancellationTokenSource();
            _ctsCanceled = new CancellationTokenSource();
            _ctPause = _ctsPaused.Token;
            _ctCancel = _ctsCanceled.Token;
            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_ctPause, _ctCancel).Token;

            // doesn't support multiple streams or each steam under 1 MB
            if (!this.SupportsResume || chunkSize <= MEGABYTE)
            {
                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get
                };
                requests.Add(request);
            }
            else
            {
                // Set up the requests
                long startPos = 0;
                if (bytesDownloadedPreviously > 0) startPos = bytesDownloadedPreviously + 1;

                for (int i = 0; i < numStreams; i++)
                {
                    long fromPos = startPos + chunkSize * i;
                    long toPos;

                    if (i == 0 && startPos == 0) fromPos = 0;
                    if (i == numStreams - 1) toPos = this.TotalBytesToDownload ?? 0;
                    else toPos = fromPos + chunkSize - 1;

                    var request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get,
                        Headers = { Range = new RangeHeaderValue(fromPos, toPos) }
                    };

                    requests.Add(request);
                }
            }

            if (this.SupportsResume)
            {
                /*if (this.TotalBytesToDownload <= KILOBYTE)
                {
                    progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 1;
                }
                else if (this.TotalBytesToDownload <= MEGABYTE)
                {
                    progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 10;
                }
                else
                {
                    progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 100;
                }*/
                progressReportingFrequency = (this.TotalBytesToDownload??0) / 100;
                if (progressReportingFrequency < bufferLength) progressReportingFrequency = bufferLength;

                nextProgressReportAt = progressReportingFrequency;
            }

            this.NumberOfActiveStreams = 0;
            AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Set up the tasks to process the requests
            foreach (var request in requests)
            {
                Task t = Task.Run(async () =>
                {
                    var sw = new Stopwatch();

                    this.NumberOfActiveStreams += 1;
                    AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));
                    try
                    {
                        if (!Directory.Exists(Path.GetDirectoryName(this.Destination)))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(this.Destination));
                        }

                        FileStream destinationStream;

                        if (requests.Count == 1)
                        {
                            destinationStream = new FileStream(this.Destination, FileMode.Append);
                        }
                        else
                        {
                            destinationStream = new FileStream(this.Destination + ".part" + requests.IndexOf(request), FileMode.Append);
                        }

                        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        using (var sourceStream = await response.Content.ReadAsStreamAsync())
                        using (var binaryWriter = new BinaryWriter(destinationStream))
                        {
                            byte[] buffer = new byte[bufferLength];
                            int t_bytes = 0;
                            int read;

                            while (true)
                            {
                                if (linkedToken.IsCancellationRequested)
                                    break;

                                sw.Start();

                                if ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) == 0)
                                {
                                    // Stream complete
                                    break;
                                }
                                else
                                {
                                    byte[] data = new byte[read];
                                    buffer.ToList().CopyTo(0, data, 0, read);
                                    binaryWriter.Write(data, 0, data.Length);
                                    t_bytes += read;
                                    streamProgress.Report(data.Length);
                                }

                                if (sw.ElapsedMilliseconds > 0 && Settings.Default.MaxDownloadSpeed > 0)
                                {
                                    sw.Stop();
                                    {
                                        long e_bytes = (long)(double)(Settings.Default.MaxDownloadSpeed * KILOBYTE / 1000 * sw.ElapsedMilliseconds);
                                        if (read > e_bytes)
                                        {
                                            double delay = (double)sw.ElapsedMilliseconds / e_bytes * (read - e_bytes);
                                            if (delay > 0)
                                            {
                                                await Task.Delay((int)delay * 1000);
                                            }
                                        }
                                    }
                                }
                                sw.Reset();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                    finally
                    {
                        this.NumberOfActiveStreams -= 1;
                        AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));
                    }
                });

                tasks.Add(t);
            }

            StartMeasuringSpeed();

            try
            {
                // Run the tasks
                await Task.WhenAll(tasks);

                this.NumberOfActiveStreams = null;
                AnnouncePropertyChanged(nameof(this.NumberOfActiveStreams));

                // Merge the streams
                if (requests.Count > 1)
                {
                    this.Status = DownloadStatus.Merging;
                    AnnouncePropertyChanged(nameof(this.Status));

                    FileInfo[] files = new FileInfo[requests.Count];
                    for (int i = 0; i < files.Length; i++)
                    {
                        files[i] = new FileInfo(this.Destination + ".part" + i);
                    }
                    await MergeFilesAsync(files);
                }
            }
            catch (Exception ex)
            {
                status = DownloadStatus.Error;
                Debug.WriteLine(ex.Message);
            }

            // if final size was unknown, update it
            if (!this.SupportsResume || this.TotalBytesToDownload < this.TotalBytesCompleted)
            {
                this.TotalBytesToDownload = this.TotalBytesCompleted;
            }

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
                long length = new FileInfo(this.Destination).Length;
                if (File.Exists(this.Destination) && length >= this.TotalBytesToDownload)
                {
                    status = DownloadStatus.Finished;
                    if (this.SupportsResume && this.TotalBytesCompleted < this.TotalBytesToDownload)
                    {
                        this.TotalBytesCompleted = this.TotalBytesToDownload ?? 0;
                    }
                    if (this.Progress < 100)
                    {
                        this.Progress = 100;
                        AnnouncePropertyChanged(nameof(this.Progress));
                    }
                    if (this.TotalBytesToDownload < this.TotalBytesCompleted)
                    {
                        this.TotalBytesToDownload = this.TotalBytesCompleted;
                    }
                }
            }

            AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
            AnnouncePropertyChanged(nameof(this.PrettyTotalSize));

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
            _semaphore.Wait();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
            _semaphore.Release();
        }

        private bool IsDownloadComplete()
        {
            if (File.Exists(this.Destination))
            {
                return (this.TotalBytesToDownload != null &&
                        this.TotalBytesToDownload > 0 &&
                        new FileInfo(this.Destination).Length >= this.TotalBytesToDownload);
            }
            return false;
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
            OnEventOccurrence(ItemEnqueued);
        }

        public void Dequeue()
        {
            if (this.IsQueued && !this.IsBeingDownloaded)
            {
                this.IsQueued = false;
                AnnouncePropertyChanged(nameof(this.IsQueued));
                OnEventOccurrence(ItemDequeued);

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
                return false;
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
            OnEventOccurrence(DownloadStarted);

            await _taskCompletion.Task;

            switch (_taskCompletion.Task.Result)
            {
                case (DownloadStatus.Error):
                case (DownloadStatus.Ready): // a.k.a canceled
                    this.Cancel();
                    break;

                case (DownloadStatus.Paused):
                    this.Pause();
                    break;

                case (DownloadStatus.Finished):
                    this.Dequeue();
                    break;

                default:
                    this.Cancel();
                    break;
            }

            this.Status = _taskCompletion.Task.Result;
            AnnouncePropertyChanged(nameof(this.Status));
            OnEventOccurrence(DownloadFinished);

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

                /*this.Status = DownloadStatus.Pausing;
                AnnouncePropertyChanged(nameof(this.Status));*/
            }
            /*else if (_taskCompletion.Task.Result == DownloadStatus.Paused)
            {
                // Download paused; update status
                this.Status = DownloadStatus.Paused;
                AnnouncePropertyChanged(nameof(this.Status));
            }*/
        }

        public async Task PauseAsync()
        {
            await Task.Run(() => this.Pause());
            _taskCompletion?.Task.Wait();
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
