using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AMDownloader.Properties;
using static AMDownloader.Common;

namespace AMDownloader
{
    delegate void RefreshCollection();

    class InvalidUrlException : Exception
    {
        public InvalidUrlException()
        {
        }
        public InvalidUrlException(string message)
            : base(message)
        {
        }
        public InvalidUrlException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    class DownloaderObjectModel : INotifyPropertyChanged, IQueueable
    {
        #region Fields
        private const int BUFFER_LENGTH = 1024;
        private const string PART_EXTENSION = ".AMDownload";
        private CancellationTokenSource _ctsPaused, _ctsCanceled;
        private CancellationToken _ctPause, _ctCancel;
        private HttpClient _httpClient;
        private TaskCompletionSource<DownloadStatus> _taskCompletion;
        private readonly RefreshCollection _refreshCollectionDel;
        private readonly SemaphoreSlim _semaphoreDownloading;
        private IProgress<long> _reportBytes;
        private bool _isCompleted;
        #endregion // Fields

        #region Events
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler DownloadInitializing;
        public event EventHandler DownloadInitialized;
        public event EventHandler DownloadStarted;
        public event EventHandler DownloadStopped;
        public event EventHandler DownloadEnqueued;
        public event EventHandler DownloadDequeued;
        public event EventHandler DownloadFinished;
        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }
        #endregion // Events

        #region Properties
        public enum DownloadStatus
        {
            Ready, Queued, Downloading, Paused, Pausing, Finished, Error, Cancelling, Connecting, Merging, Verifying
        }
        public long? TotalBytesToDownload { get; private set; } // nullable
        public long? Speed { get; private set; } // nullable
        public int? NumberOfActiveStreams { get; private set; } // nullable    
        public HttpStatusCode? StatusCode { get; private set; } // nullable
        public bool IsBeingDownloaded => _ctsPaused != null;
        public bool IsCompleted => File.Exists(this.Destination) && _isCompleted;
        public bool IsQueued { get; private set; }
        public string Name { get; private set; }
        public string Url { get; private set; }
        public string Destination { get; private set; }
        public DownloadStatus Status { get; private set; }
        public int Progress { get; private set; }
        public long TotalBytesCompleted { get; private set; }
        public long BytesDownloadedThisSession { get; private set; }
        public bool SupportsResume { get; private set; }
        public DateTime DateCreated { get; private set; }
        #endregion // Properties

        #region Constructors
        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            bool isCompleted,
            EventHandler downloadInitializingEventHandler,
            EventHandler downloadInitializedEventHandler,
            EventHandler downloadStartedEventHandler,
            EventHandler downloadStoppedEventHandler,
            EventHandler downloadEnqueuedEventHandler,
            EventHandler downloadDequeuedEventHandler,
            EventHandler downloadFinishedEventHandler,
            PropertyChangedEventHandler propertyChangedEventHandler,
            RefreshCollection refreshCollectionDelegate,
            IProgress<long> bytesReporter)
        {
            _httpClient = httpClient;
            _refreshCollectionDel = refreshCollectionDelegate;
            _semaphoreDownloading = new SemaphoreSlim(1);
            _reportBytes = bytesReporter;
            _isCompleted = isCompleted;

            PropertyChanged += propertyChangedEventHandler;
            DownloadInitializing += downloadInitializingEventHandler;
            DownloadInitialized += downloadInitializedEventHandler;
            DownloadStarted += downloadStartedEventHandler;
            DownloadStopped += downloadStoppedEventHandler;
            DownloadEnqueued += downloadEnqueuedEventHandler;
            DownloadDequeued += downloadDequeuedEventHandler;
            DownloadFinished += downloadFinishedEventHandler;

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
            this.StatusCode = null;
            if (enqueue) this.Enqueue();
            Task.Run(async () => await VerifyDownloadAsync(url));
        }
        #endregion // Constructors

        #region Private methods
        private void SetFinished()
        {
            if (this.IsQueued) this.Dequeue();
            this.Status = DownloadStatus.Finished;
            this.Progress = 100;
            this.TotalBytesToDownload = new FileInfo(this.Destination).Length;
            this.TotalBytesCompleted = (this.TotalBytesToDownload ?? 0);
            RaisePropertyChanged(nameof(this.TotalBytesToDownload));
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }
        private void SetErrored()
        {
            if (this.IsQueued) this.Dequeue();
            this.Status = DownloadStatus.Error;
            this.Progress = 0;
            this.TotalBytesCompleted = 0;
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }
        private void SetPaused()
        {
            this.Progress = (int)((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100);
            this.Status = DownloadStatus.Paused;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }
        private void SetQueued()
        {
            this.IsQueued = true;
            if (this.Status != DownloadStatus.Paused) this.Status = DownloadStatus.Queued;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.IsQueued));
        }
        private void SetDequeued()
        {
            this.IsQueued = false;
            if (this.Status == DownloadStatus.Queued) this.Status = DownloadStatus.Ready;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.IsQueued));
        }
        private void SetReady()
        {
            this.TotalBytesCompleted = 0;
            this.Progress = 0;
            this.Status = DownloadStatus.Ready;
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }

        private async Task VerifyDownloadAsync(string url)
        {
            await _semaphoreDownloading.WaitAsync();

            this.Status = DownloadStatus.Verifying;
            RaisePropertyChanged(nameof(this.Status));
            RaiseEvent(DownloadInitializing);

            if (this.IsCompleted)
            {
                // download complete
                SetFinished();
            }
            else
            {
                this.StatusCode = await VerifyUrlValidityAsync(url);

                if (this.StatusCode != HttpStatusCode.OK)
                {
                    // invalid url
                    SetErrored();
                }
                else
                {
                    // valid url
                    this.TotalBytesToDownload = await VerifyTotalBytesToDownloadAsync(url);
                    if (this.TotalBytesToDownload > 0) this.SupportsResume = true;

                    if (File.Exists(this.Destination) && this.SupportsResume)
                    {
                        // download paused
                        this.TotalBytesCompleted = new FileInfo(this.Destination).Length;
                        SetPaused();
                    }
                    else
                    {
                        // new download
                        if (this.IsQueued)
                        {
                            SetQueued();
                        }
                        else
                        {
                            SetReady();
                        }
                    }
                }
            }

            if (this.IsCompleted) RaiseEvent(DownloadFinished);
            RaiseEvent(DownloadInitialized);
            _refreshCollectionDel?.Invoke();
            _semaphoreDownloading.Release();
        }

        private async Task<HttpStatusCode?> VerifyUrlValidityAsync(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using (HttpResponseMessage response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    return response.StatusCode;
                }
            }
            catch (HttpRequestException)
            {
                return HttpStatusCode.BadRequest;
            }
            catch
            {
                return null;
            }
        }
        private async Task<long?> VerifyTotalBytesToDownloadAsync(string url)
        {
            try
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url);
                HttpResponseMessage response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return response.Content.Headers.ContentLength;
            }
            catch
            {
                return null;
            }
        }

        private async Task MergeFilesAsync(int partCount, params FileInfo[] files)
        {
            // split original into 5
            /*if (File.Exists(this.Destination))
            {
                var chunkSize = new FileInfo(this.Destination).Length / partCount;
                var sourceStreams = new FileStream[partCount];
                // var sourceWriters = new BinaryWriter[partCount];
                var fs = new FileStream(this.Destination, FileMode.Open, FileAccess.Read);
                var reader = new BinaryReader(fs);
                for (int i = 0; i < partCount; i++)
                {
                    var data = new byte[chunkSize];
                    sourceStreams[i] = File.Create(this.Destination + ".source" + i + PART_EXTENSION);
                    reader.BaseStream.Seek(i * partCount, SeekOrigin.Begin);
                    reader.Read(data, 0, data.Length);
                    sourceStreams[i].Write(data);
                    sourceStreams[i].Close();
                }
                reader.Close();
                fs.Close();
            }*/

            using (var destStream = new FileStream(this.Destination, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                using (var binaryWriter = new BinaryWriter(destStream))
                {
                    for (int i = 0; i < partCount; i++)
                    {
                        using (var sourceStream = new FileStream(files[i].FullName, FileMode.Open))
                        {
                            await sourceStream.CopyToAsync(destStream, BUFFER_LENGTH);
                        }
                        files[i].Delete();
                    }
                }
            }
        }

        private async Task<DownloadStatus> ProcessStreamsAsync(long bytesDownloadedPreviously = 0)
        {
            bool completedSuccessfully = true;
            var status = DownloadStatus.Error;
            List<HttpRequestMessage> requests = new List<HttpRequestMessage>();
            List<Task> tasks = new List<Task>();
            long progressReportingFrequency = BUFFER_LENGTH;
            long nextProgressReportAt = BUFFER_LENGTH;
            long maxDownloadSpeed = Settings.Default.MaxDownloadSpeed * 1024;
            int numStreams = Settings.Default.MaxConnectionsPerDownload;
            SemaphoreSlim semaphoreProgress = new SemaphoreSlim(1);
            IProgress<int> streamProgress = new Progress<int>(async (value) =>
            {
                await semaphoreProgress.WaitAsync();
                this.BytesDownloadedThisSession += value;
                this.TotalBytesCompleted = this.BytesDownloadedThisSession + bytesDownloadedPreviously;
                _reportBytes.Report(value);

                if (this.SupportsResume && (this.TotalBytesCompleted >= nextProgressReportAt || this.TotalBytesCompleted > this.TotalBytesToDownload - progressReportingFrequency))
                {
                    double progress = (double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100;
                    this.Progress = (int)progress;
                    RaisePropertyChanged(nameof(this.Progress));
                    RaisePropertyChanged(nameof(this.TotalBytesCompleted));
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
                if (bytesDownloadedPreviously > 0) startPos = bytesDownloadedPreviously;

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
                if (this.TotalBytesToDownload <= KILOBYTE)
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
                }

                if (progressReportingFrequency < BUFFER_LENGTH)
                    progressReportingFrequency = BUFFER_LENGTH;
                else if (progressReportingFrequency > 512 * KILOBYTE)
                    progressReportingFrequency = 512 * KILOBYTE;

                nextProgressReportAt = progressReportingFrequency;
            }

            // Set up the tasks to process the requests

            foreach (var request in requests)
            {
                Task t = Task.Run(async () =>
                {
                    FileStream destinationStream = null;
                    try
                    {
                        if (!Directory.Exists(Path.GetDirectoryName(this.Destination)))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(this.Destination));
                        }
                        if (requests.Count > 1)
                        {
                            destinationStream = new FileStream(this.Destination + ".part" + requests.IndexOf(request) + PART_EXTENSION, FileMode.Append);
                        }
                        else
                        {
                            destinationStream = new FileStream(this.Destination, FileMode.Append);
                        }
                        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        using (var sourceStream = await response.Content.ReadAsStreamAsync())
                        using (var binaryWriter = new BinaryWriter(destinationStream))
                        {
                            byte[] buffer = new byte[BUFFER_LENGTH];
                            int s_bytesReceived = 0;
                            int read;
                            var stopWatch = new Stopwatch();
                            while (true)
                            {
                                linkedToken.ThrowIfCancellationRequested();

                                stopWatch.Start();
                                read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, linkedToken);
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
                                    s_bytesReceived += read;
                                    streamProgress.Report(data.Length);
                                }

                                stopWatch.Stop();

                                // Speed throttler
                                if (maxDownloadSpeed > 0 && stopWatch.ElapsedMilliseconds > 0)
                                {
                                    int s_bytesExpected = (int)((double)maxDownloadSpeed / 1000 * stopWatch.ElapsedMilliseconds);
                                    if (s_bytesReceived > s_bytesExpected)
                                    {
                                        long expectedMilliseconds = (long)(1000 / (double)maxDownloadSpeed * s_bytesReceived);
                                        long delay = expectedMilliseconds - stopWatch.ElapsedMilliseconds;
                                        if (delay > 0) await Task.Delay((int)delay);
                                        s_bytesReceived = 0;
                                        stopWatch.Reset();
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        completedSuccessfully = false;
                        return;
                    }
                    finally
                    {
                        destinationStream?.Close();
                    }
                }).ContinueWith(t =>
                {
                    this.NumberOfActiveStreams--;
                    RaisePropertyChanged(nameof(this.NumberOfActiveStreams));
                });
                tasks.Add(t);
            }

            StartMeasuringSpeed();
            this.NumberOfActiveStreams = requests.Count;
            RaisePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Run the tasks
            await Task.WhenAll(tasks);

            this.NumberOfActiveStreams = null;
            RaisePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Merge the streams
            if (requests.Count > 1)
            {
                this.Status = DownloadStatus.Merging;
                RaisePropertyChanged(nameof(this.Status));
                FileInfo[] files = new FileInfo[requests.Count];
                for (int i = 0; i < files.Length; i++)
                {
                    files[i] = new FileInfo(this.Destination + ".part" + i + PART_EXTENSION);
                }
                await MergeFilesAsync(numStreams, files);
            }

            // Update final size
            if (!this.SupportsResume) this.TotalBytesToDownload = this.TotalBytesCompleted;

            // Operation complete; verify state
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
            else if (completedSuccessfully)
            {
                status = DownloadStatus.Finished;
            }
            else
            {
                status = DownloadStatus.Error;
            }

            _ctsCanceled = null;
            _ctsPaused = null;
            _ctCancel = default;
            _ctPause = default;

            _isCompleted = completedSuccessfully;
            _taskCompletion.SetResult(status);
            return status;
        }

        private void StartMeasuringSpeed()
        {
            long fromBytes;
            long toBytes;
            long bytesCaptured;
            Task.Run(async () =>
            {
                while (this.IsBeingDownloaded)
                {
                    fromBytes = this.TotalBytesCompleted;
                    await Task.Delay(1000);
                    toBytes = this.TotalBytesCompleted;
                    bytesCaptured = toBytes - fromBytes;
                    if (bytesCaptured >= 0)
                    {
                        this.Speed = bytesCaptured;
                        RaisePropertyChanged(nameof(this.Speed));
                    }
                }
            }).ContinueWith(t =>
            {
                this.Speed = null;
                RaisePropertyChanged(nameof(this.Speed));
            });
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
        #endregion // Private methods

        #region Public methods
        public void Enqueue()
        {
            if (this.IsQueued || this.Status != DownloadStatus.Ready) return;
            SetQueued();
            RaiseEvent(DownloadEnqueued);
        }

        public void Dequeue()
        {
            if (this.IsQueued && !this.IsBeingDownloaded)
            {
                SetDequeued();
                RaiseEvent(DownloadDequeued);
            }
        }

        public async Task StartAsync()
        {
            await _semaphoreDownloading.WaitAsync();
            long bytesAlreadyDownloaded = 0;
            if (_ctsPaused != null)
            {
                // Download in progress
                return;
            }
            else if (_ctsCanceled != null || (_ctsPaused == null && _ctsCanceled == null))
            {
                if (this.IsCompleted)
                {
                    // Don't start an already completed download
                    SetFinished();
                    return;
                }
                else
                {
                    this.Status = DownloadStatus.Connecting;
                    RaisePropertyChanged(nameof(this.Status));

                    // Ensure url is valid for all downloads
                    if (await VerifyUrlValidityAsync(this.Url) != HttpStatusCode.OK)
                    {
                        SetErrored();
                        return;
                    }

                    if (File.Exists(this.Destination))
                    {
                        // Download is paused; resume from specific point
                        bytesAlreadyDownloaded = new FileInfo(this.Destination).Length;
                    }
                }
            }

            _taskCompletion = new TaskCompletionSource<DownloadStatus>(ProcessStreamsAsync(bytesAlreadyDownloaded));

            this.BytesDownloadedThisSession = 0;
            this.TotalBytesCompleted = bytesAlreadyDownloaded;
            this.Status = DownloadStatus.Downloading;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            RaiseEvent(DownloadStarted);

            await _taskCompletion.Task;

            switch (_taskCompletion.Task.Result)
            {
                case (DownloadStatus.Error):
                    this.Cancel();
                    SetErrored();
                    break;

                case (DownloadStatus.Ready): // a.k.a canceled
                    this.Cancel();
                    this.Dequeue();
                    SetReady();
                    break;

                case (DownloadStatus.Paused):
                    this.Pause();
                    SetPaused();
                    break;

                case (DownloadStatus.Finished):
                    SetFinished();
                    break;

                default:
                    this.Cancel();
                    this.Dequeue();
                    SetReady();
                    break;
            }

            _refreshCollectionDel?.Invoke();
            _semaphoreDownloading.Release();
            RaiseEvent(DownloadStopped);
            if (_taskCompletion.Task.Result == DownloadStatus.Finished) RaiseEvent(DownloadFinished);
        }

        public void Pause()
        {
            if (this.IsCompleted || _taskCompletion == null)
            {
                // Nothing to pause
                return;
            }

            if (_ctsPaused != null)
            {
                // Download in progress; request cancellation
                _ctsPaused.Cancel();
            }
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
                RaisePropertyChanged(nameof(this.Status));
            }
            else if (!this.IsCompleted)
            {
                // Cancellation complete; delete partially downloaded file
                Task.Run(async () =>
                {
                    int numRetries = 30;
                    while (File.Exists(this.Destination) && numRetries-- > 0)
                    {
                        // if deletion fails, retry 30 times with 1 sec interval
                        try
                        {
                            File.Delete(this.Destination);
                            SetReady();
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
                });
            }
        }

        public void SetCreationTime(DateTime newDateCreated)
        {
            this.DateCreated = newDateCreated;
            RaisePropertyChanged(nameof(this.DateCreated));
        }
        #endregion // Public methods
    }
}
