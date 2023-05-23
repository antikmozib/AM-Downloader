// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using AMDownloader.RequestThrottling;
using AMDownloader.RequestThrottling.Model;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AMDownloader.ObjectModel
{
    internal enum DownloadStatus
    {
        Ready, Queued, Downloading, Paused, Pausing, Finishing, Finished, Error, Cancelling, Connecting, Merging, Verifying
    }

    internal class DownloaderObjectModel : INotifyPropertyChanged, IQueueable
    {
        #region Fields

        private CancellationTokenSource _ctsPaused, _ctsCanceled;
        private CancellationToken _ctPause, _ctCancel;
        private TaskCompletionSource<DownloadStatus> _taskCompletion;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _semaphoreDownloading;
        private readonly IProgress<long> _reportBytesProgress;
        private readonly RequestThrottler _requestThrottler;

        #endregion Fields

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler DownloadCreated;

        public event EventHandler DownloadVerifying;

        public event EventHandler DownloadVerified;

        public event EventHandler DownloadStarted;

        public event EventHandler DownloadStopped;

        public event EventHandler DownloadEnqueued;

        public event EventHandler DownloadDequeued;

        public event EventHandler DownloadFinished;

        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }

        #endregion Events

        #region Properties

        public string TempDestination { get; }
        public long? TotalBytesToDownload { get; private set; } // nullable
        public long? Speed { get; private set; } // nullable
        public int? NumberOfActiveStreams { get; private set; } // nullable
        public HttpStatusCode? StatusCode { get; private set; } // nullable
        public double? TimeRemaining { get; private set; } // nullable
        public bool IsBeingDownloaded => _ctsPaused != null;
        public bool IsCompleted => File.Exists(this.Destination) && !File.Exists(TempDestination);
        public bool IsPaused => File.Exists(TempDestination) && !File.Exists(this.Destination);
        public bool IsQueued { get; private set; }
        public string Name { get; private set; }
        public string Extension => Path.GetExtension(this.Destination);
        public string Url { get; private set; }
        public string Destination { get; private set; }
        public DownloadStatus Status { get; private set; }
        public int Progress { get; private set; }
        public long TotalBytesCompleted { get; private set; }
        public long BytesDownloadedThisSession { get; private set; }
        public bool SupportsResume { get; private set; }
        public DateTime DateCreated { get; private set; }

        #endregion Properties

        #region Constructors

        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            EventHandler downloadCreatedEventHandler,
            EventHandler downloadVerifyingEventHandler,
            EventHandler downloadVerifiedEventHandler,
            EventHandler downloadStartedEventHandler,
            EventHandler downloadStoppedEventHandler,
            EventHandler downloadEnqueuedEventHandler,
            EventHandler downloadDequeuedEventHandler,
            EventHandler downloadFinishedEventHandler,
            PropertyChangedEventHandler propertyChangedEventHandler,
            IProgress<long> bytesReporter,
            ref RequestThrottler requestThrottler) : this(
                ref httpClient,
                url,
                destination,
                enqueue,
                totalBytesToDownload: null,
                httpStatusCode: null,
                downloadCreatedEventHandler,
                downloadVerifyingEventHandler,
                downloadVerifiedEventHandler,
                downloadStartedEventHandler,
                downloadStoppedEventHandler,
                downloadEnqueuedEventHandler,
                downloadDequeuedEventHandler,
                downloadFinishedEventHandler,
                propertyChangedEventHandler,
                bytesReporter,
                ref requestThrottler)
        { }

        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            long? totalBytesToDownload,
            HttpStatusCode? httpStatusCode,
            EventHandler downloadCreatedEventHandler,
            EventHandler downloadVerifyingEventHandler,
            EventHandler downloadVerifiedEventHandler,
            EventHandler downloadStartedEventHandler,
            EventHandler downloadStoppedEventHandler,
            EventHandler downloadEnqueuedEventHandler,
            EventHandler downloadDequeuedEventHandler,
            EventHandler downloadFinishedEventHandler,
            PropertyChangedEventHandler propertyChangedEventHandler,
            IProgress<long> bytesReporter,
            ref RequestThrottler requestThrottler)
        {
            _httpClient = httpClient;
            _semaphoreDownloading = new SemaphoreSlim(1);
            _reportBytesProgress = bytesReporter;
            _requestThrottler = requestThrottler;
            TempDestination = destination + AppConstants.DownloaderSplitedPartExtension;

            PropertyChanged += propertyChangedEventHandler;
            DownloadCreated += downloadCreatedEventHandler;
            DownloadVerifying += downloadVerifyingEventHandler;
            DownloadVerified += downloadVerifiedEventHandler;
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
            this.SupportsResume = false;
            this.Speed = null;
            this.DateCreated = DateTime.Now;
            this.NumberOfActiveStreams = null;
            this.StatusCode = null;
            this.TimeRemaining = null;
            this.IsQueued = false;

            if (enqueue)
            {
                this.Enqueue();
            }

            Task.Run(async () =>
            {
                _semaphoreDownloading.Wait();
                if (totalBytesToDownload > 0)
                {
                    var restore = new RequestModel();
                    restore.TotalBytesToDownload = totalBytesToDownload;
                    restore.StatusCode = httpStatusCode;
                    restore.Url = url;
                    await VerifyDownloadAsync(url, restore);
                }
                else
                {
                    await VerifyDownloadAsync(url);
                }
                _semaphoreDownloading.Release();
            });

            RaiseEvent(DownloadCreated);
        }

        #endregion Constructors

        #region Private methods

        private void SetFinished()
        {
            this.IsQueued = false;
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
            this.IsQueued = false;
            this.Status = DownloadStatus.Error;
            this.Progress = 0;
            this.TotalBytesCompleted = 0;
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }

        private void SetPaused()
        {
            if (!this.SupportsResume)
            {
                this.Progress = 0;
            }
            else
            {
                this.Progress = (int)((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100);
            }
            this.Status = DownloadStatus.Paused;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }

        private void SetQueued()
        {
            this.IsQueued = true;
            RaisePropertyChanged(nameof(this.IsQueued));
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            if (this.Status != DownloadStatus.Paused)
            {
                this.Status = DownloadStatus.Queued;
                RaisePropertyChanged(nameof(this.Status));
            }
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
            this.IsQueued = false;
            this.Status = DownloadStatus.Ready;
            RaisePropertyChanged(nameof(this.TotalBytesToDownload));
            RaisePropertyChanged(nameof(this.StatusCode));
            RaisePropertyChanged(nameof(this.TotalBytesCompleted));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }

        private async Task VerifyDownloadAsync(string url, RequestModel? restore = null)
        {
            // verifies the download; uses RequestThrottler to throttle multiple concurrent requests to the same url
            this.Status = DownloadStatus.Verifying;
            RaisePropertyChanged(nameof(this.Status));
            RaiseEvent(DownloadVerifying);

            if (this.IsCompleted)
            {
                // finished download
                SetFinished();
            }
            else
            {
                // are we restoring? only restore if we have a valid status code
                if (restore != null && restore?.StatusCode != null)
                {
                    this.TotalBytesToDownload = restore?.TotalBytesToDownload;
                    this.StatusCode = restore?.StatusCode;
                }
                else
                {
                    // check if we've seen this recently and throttle requests
                    RequestModel? requestModel = _requestThrottler.Has(url);
                    if (requestModel != null)
                    {
                        // we've seen this *recently*; just grab seen data
                        this.TotalBytesToDownload = requestModel?.TotalBytesToDownload;
                        this.StatusCode = requestModel?.StatusCode;
                    }
                    else
                    {
                        // we haven't seen this before; verify actual url
                        var urlVerification = await VerifyUrlAsync(url);
                        this.StatusCode = urlVerification.StatusCode;
                        this.TotalBytesToDownload = urlVerification.TotalBytesToDownload;
                    }
                }

                RaisePropertyChanged(nameof(this.StatusCode));
                RaisePropertyChanged(nameof(this.TotalBytesToDownload));

                if (this.TotalBytesToDownload > 0)
                {
                    this.SupportsResume = true;
                    RaisePropertyChanged(nameof(this.SupportsResume));
                }

                if (IsValidStatus(this.StatusCode) || IsErroredStatus(this.StatusCode))
                {
                    _requestThrottler.Keep(url, TotalBytesToDownload, StatusCode);
                }

                if (!IsValidStatus(this.StatusCode))
                {
                    // invalid url
                    SetErrored();
                }
                else
                {
                    // valid url
                    if (File.Exists(TempDestination))
                    {
                        this.TotalBytesCompleted = new FileInfo(TempDestination).Length;
                    }
                    else
                    {
                        this.TotalBytesCompleted = 0;
                    }

                    if (this.IsPaused)
                    {
                        // paused download
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
            RaiseEvent(DownloadVerified);
        }

        private async Task<UrlVerificationModel> VerifyUrlAsync(string url, int retry = 5)
        {
            // force checks the url and returns TotalBytesToDownload
            UrlVerificationModel urlVerification;
            urlVerification.StatusCode = null;
            urlVerification.TotalBytesToDownload = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using HttpResponseMessage response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                urlVerification.StatusCode = response.StatusCode;
                urlVerification.TotalBytesToDownload = response.Content.Headers.ContentLength;
            }
            catch (Exception ex)
            {
                if (retry > 0)
                {
                    --retry;
                    await Task.Delay(1000 * (5 - retry));
                    return await VerifyUrlAsync(url, retry);
                }
                else
                {
                    if (ex is HttpRequestException)
                    {
                        urlVerification.StatusCode = HttpStatusCode.BadRequest;
                    }
                    else
                    {
                        urlVerification.StatusCode = null;
                    }
                }
            }

            return urlVerification;
        }

        private bool CreateEmptyDownload(string filename)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(filename)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filename));
                }

                var tempFile = filename + AppConstants.DownloaderSplitedPartExtension;

                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                File.Create(tempFile).Close();

                // ensure this is an empty download by checking length
                if (File.Exists(tempFile) && new FileInfo(tempFile).Length == 0 && !File.Exists(filename))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                throw new AMDownloaderException("Failed to create new download: " + filename, ex);
            }
        }

        private async Task<DownloadStatus> ProcessStreamsAsync(long bytesDownloadedPreviously)
        {
            var status = DownloadStatus.Error;
            Task<bool> streamTask;
            HttpRequestMessage request;
            long maxDownloadSpeed = Settings.Default.MaxDownloadSpeed * 1024;
            SemaphoreSlim semaphoreProgress = new SemaphoreSlim(1);
            IProgress<int> streamProgress = new Progress<int>(async (value) =>
            {
                await semaphoreProgress.WaitAsync();
                this.BytesDownloadedThisSession += value;
                this.TotalBytesCompleted = this.BytesDownloadedThisSession + bytesDownloadedPreviously;
                if (!this.SupportsResume)
                {
                    this.TotalBytesToDownload = this.TotalBytesCompleted;
                }
                _reportBytesProgress.Report(value);
                double progress = (double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100;
                this.Progress = (int)progress;
                semaphoreProgress.Release();
            });

            // doesn't support multiple streams or each steam under 1 MB
            if (!this.SupportsResume)
            {
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get
                };
            }
            else
            {
                // Set up the request
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get,
                    Headers = { Range = new RangeHeaderValue(bytesDownloadedPreviously, this.TotalBytesToDownload) }
                };
            }

            _ctsPaused = new CancellationTokenSource();
            _ctsCanceled = new CancellationTokenSource();
            _ctPause = _ctsPaused.Token;
            _ctCancel = _ctsCanceled.Token;
            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_ctPause, _ctCancel).Token;

            // Process the request
            streamTask = Task.Run(async () =>
            {
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(this.Destination)))
                    {
                        throw new AMDownloaderException("Destination directory does not exist.");
                    }
                    if (!File.Exists(TempDestination) && File.Exists(this.Destination))
                    {
                        throw new AMDownloaderException("A new download has not been created.");
                    }

                    using var destinationStream = new FileStream(TempDestination, FileMode.Append, FileAccess.Write);
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    using var sourceStream = await response.Content.ReadAsStreamAsync();
                    using var binaryWriter = new BinaryWriter(destinationStream);
                    byte[] buffer = new byte[AppConstants.DownloaderStreamBufferLength];
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
                            request.Dispose();
                            return true;
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
                catch (Exception ex)
                {
                    throw new AMDownloaderException(ex.Message, ex);
                }
            });

            StartReportingProgress();
            StartMeasuringEta();

            try
            {
                // Run the tasks
                var finished = await streamTask;

                // Operation complete; verify state
                // completed successfully
                if (finished)
                {
                    status = DownloadStatus.Finished;
                }
                else
                {
                    status = DownloadStatus.Error;
                }
            }
            catch
            {
                // Paused, cancelled or errored
                if (!linkedToken.IsCancellationRequested)
                {
                    status = DownloadStatus.Error;
                }
                else if (_ctPause.IsCancellationRequested)
                {
                    status = DownloadStatus.Paused;
                }
                else if (_ctCancel.IsCancellationRequested)
                {
                    status = DownloadStatus.Ready;
                }
            }

            // Update final size
            if (!this.SupportsResume) this.TotalBytesToDownload = this.TotalBytesCompleted;

            _ctsPaused = null;
            _ctsCanceled = null;
            _ctPause = default;
            _ctCancel = default;
            _taskCompletion.SetResult(status);
            return status;
        }

        private void StartReportingProgress()
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
                    RaisePropertyChanged(nameof(this.TotalBytesCompleted));
                    if (this.SupportsResume)
                    {
                        RaisePropertyChanged(nameof(this.Progress));
                    }
                    else
                    {
                        RaisePropertyChanged(nameof(this.TotalBytesToDownload));
                    }
                }
            }).ContinueWith(t =>
            {
                this.Speed = null;
                RaisePropertyChanged(nameof(this.Speed));
            });
        }

        private void StartMeasuringEta()
        {
            if (!this.SupportsResume)
            {
                this.TimeRemaining = null;
                RaisePropertyChanged(nameof(this.TimeRemaining));
            }
            else
            {
                Task.Run(async () =>
                {
                    long bytesFrom = this.TotalBytesCompleted;
                    long bytesTo;
                    long bytesCaptured;
                    Stopwatch stopWatch = new Stopwatch();

                    while (this.IsBeingDownloaded)
                    {
                        stopWatch.Start();
                        await Task.Delay(1000);
                        bytesTo = this.TotalBytesCompleted;
                        bytesCaptured = bytesTo - bytesFrom;
                        stopWatch.Stop();

                        double eta = ((this.TotalBytesToDownload ?? 0) - this.TotalBytesCompleted) *
                                        ((double)stopWatch.ElapsedMilliseconds / bytesCaptured);
                        if (eta >= 0)
                        {
                            this.TimeRemaining = eta;
                            RaisePropertyChanged(nameof(this.TimeRemaining));
                        }
                    }
                }).ContinueWith(t =>
                {
                    this.TimeRemaining = null;
                    RaisePropertyChanged(nameof(this.TimeRemaining));
                });
            }
        }

        private bool IsValidStatus(HttpStatusCode? statusCode)
        {
            if (statusCode == null) return false;

            switch (statusCode)
            {
                case HttpStatusCode.OK:
                case HttpStatusCode.PartialContent:
                    return true;
            }

            return false;
        }

        private bool IsErroredStatus(HttpStatusCode? statusCode)
        {
            if (statusCode == null) return false;

            if (statusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            return false;
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion Private methods

        #region Public methods

        public void Enqueue()
        {
            if (this.IsQueued || this.IsBeingDownloaded || this.IsCompleted) return;
            SetQueued();
            RaiseEvent(DownloadEnqueued);
        }

        public void Dequeue()
        {
            if (!this.IsQueued || this.IsBeingDownloaded) return;
            SetDequeued();
            RaiseEvent(DownloadDequeued);
        }

        public async Task StartAsync()
        {
            await _semaphoreDownloading.WaitAsync();
            try
            {
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
                        UrlVerificationModel urlVerification;
                        urlVerification = await VerifyUrlAsync(this.Url);

                        // Ensure url is valid for all downloads
                        if (!IsValidStatus(urlVerification.StatusCode))
                        {
                            SetErrored();
                            return;
                        }

                        if (this.IsPaused)
                        {
                            // Download is paused; resume from specific point
                            bytesAlreadyDownloaded = new FileInfo(TempDestination).Length;
                        }
                        else
                        {
                            if (!CreateEmptyDownload(this.Destination))
                            {
                                throw new IOException("Failed to create new download.");
                            }
                        }
                    }
                }

                _taskCompletion = new TaskCompletionSource<DownloadStatus>(ProcessStreamsAsync(bytesAlreadyDownloaded));
                this.BytesDownloadedThisSession = 0;
                this.TotalBytesCompleted = bytesAlreadyDownloaded;
                this.Status = DownloadStatus.Downloading;
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.TotalBytesCompleted));
                RaisePropertyChanged(nameof(this.IsBeingDownloaded));

                RaiseEvent(DownloadStarted);
                var result = await _taskCompletion.Task;

                switch (result)
                {
                    case DownloadStatus.Error:
                        this.Cancel();
                        SetErrored();
                        break;

                    case DownloadStatus.Ready:
                        this.Cancel();
                        SetReady();
                        break;

                    case DownloadStatus.Paused:
                        this.Pause();
                        SetPaused();
                        break;

                    case DownloadStatus.Finished:
                        this.Status = DownloadStatus.Finishing;
                        RaisePropertyChanged(nameof(this.Status));
                        int retry = 5;
                        while (retry-- > 0)
                        {
                            try
                            {
                                File.Move(TempDestination, this.Destination, true);
                                SetFinished();
                                break;
                            }
                            catch
                            {
                                await Task.Delay(1000);
                                continue;
                            }
                        }
                        break;

                    default:
                        this.Cancel();
                        SetReady();
                        break;
                }

                RaisePropertyChanged(nameof(this.IsBeingDownloaded));
                RaiseEvent(DownloadStopped);
                if (result == DownloadStatus.Finished) RaiseEvent(DownloadFinished);
            }
            finally
            {
                _semaphoreDownloading.Release();
            }
        }

        public void Pause()
        {
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
                    while (File.Exists(TempDestination) && numRetries-- > 0)
                    {
                        // if deletion fails, retry 30 times with 1 sec interval
                        try
                        {
                            File.Delete(TempDestination);
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

        public async Task CancelAsync()
        {
            await Task.Run(() => this.Cancel());
            _taskCompletion?.Task.Wait();
        }

        public void SetCreationTime(DateTime newDateCreated)
        {
            this.DateCreated = newDateCreated;
            RaisePropertyChanged(nameof(this.DateCreated));
        }

        public async Task ForceReCheckAsync()
        {
            if (this.IsBeingDownloaded) return;
            await _semaphoreDownloading.WaitAsync();
            await VerifyDownloadAsync(this.Url);
            _semaphoreDownloading.Release();
        }

        #endregion Public methods
    }
}