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
using AMDownloader.Common;
using System.Text;
using AMDownloader.ObjectModel.Queue;

namespace AMDownloader.ObjectModel
{
    delegate void RefreshCollection();
    public enum DownloadStatus
    {
        Ready, Queued, Downloading, Paused, Pausing, Finishing, Finished, Error, Cancelling, Connecting, Merging, Verifying
    }
    class DownloaderObjectModel : INotifyPropertyChanged, IQueueable
    {
        #region Fields
        private CancellationTokenSource _ctsPaused, _ctsCanceled;
        private CancellationToken _ctPause, _ctCancel;
        private HttpClient _httpClient;
        private TaskCompletionSource<DownloadStatus> _taskCompletion;
        private readonly RefreshCollection _refreshCollectionDel;
        private readonly SemaphoreSlim _semaphoreDownloading;
        private IProgress<long> _reportBytesProgress;
        private RequestThrottler _requestThrottler;
        private readonly byte[] _pausedIdentifierBytes;
        #endregion // Fields

        #region Events
        public event PropertyChangedEventHandler PropertyChanged;
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
        #endregion // Events

        #region Properties
        public long? TotalBytesToDownload { get; private set; } // nullable
        public long? Speed { get; private set; } // nullable
        public int? NumberOfActiveStreams { get; private set; } // nullable    
        public HttpStatusCode? StatusCode { get; private set; } // nullable
        public bool IsBeingDownloaded => _ctsPaused != null;
        public bool IsCompleted => File.Exists(this.Destination) && !IsFilePaused(this.Destination, _pausedIdentifierBytes);
        public bool IsPaused => File.Exists(this.Destination) && IsFilePaused(this.Destination, _pausedIdentifierBytes);
        public bool IsQueued { get; private set; }
        public string Name { get; private set; }
        public string Extension => GetExtension();
        public string Url { get; private set; }
        public string Destination { get; private set; }
        public DownloadStatus Status { get; private set; }
        public int Progress { get; private set; }
        public long TotalBytesCompleted { get; private set; }
        public long BytesDownloadedThisSession { get; private set; }
        public bool SupportsResume { get; private set; }
        public DateTime DateCreated { get; private set; }
        public double? TimeRemaining { get; private set; }
        #endregion // Properties

        #region Constructors        
        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            EventHandler downloadVerifyingEventHandler,
            EventHandler downloadVerifiedEventHandler,
            EventHandler downloadStartedEventHandler,
            EventHandler downloadStoppedEventHandler,
            EventHandler downloadEnqueuedEventHandler,
            EventHandler downloadDequeuedEventHandler,
            EventHandler downloadFinishedEventHandler,
            PropertyChangedEventHandler propertyChangedEventHandler,
            RefreshCollection refreshCollectionDelegate,
            IProgress<long> bytesReporter,
            ref RequestThrottler requestThrottler) : this(ref httpClient, url, destination, enqueue, totalBytesToDownload: null, downloadVerifyingEventHandler, downloadVerifiedEventHandler, downloadStartedEventHandler, downloadStoppedEventHandler, downloadEnqueuedEventHandler, downloadDequeuedEventHandler, downloadFinishedEventHandler, propertyChangedEventHandler, refreshCollectionDelegate, bytesReporter, ref requestThrottler) { }

        public DownloaderObjectModel(
            ref HttpClient httpClient,
            string url,
            string destination,
            bool enqueue,
            long? totalBytesToDownload,
            EventHandler downloadVerifyingEventHandler,
            EventHandler downloadVerifiedEventHandler,
            EventHandler downloadStartedEventHandler,
            EventHandler downloadStoppedEventHandler,
            EventHandler downloadEnqueuedEventHandler,
            EventHandler downloadDequeuedEventHandler,
            EventHandler downloadFinishedEventHandler,
            PropertyChangedEventHandler propertyChangedEventHandler,
            RefreshCollection refreshCollectionDelegate,
            IProgress<long> bytesReporter,
            ref RequestThrottler requestThrottler)
        {
            _httpClient = httpClient;
            _refreshCollectionDel = refreshCollectionDelegate;
            _semaphoreDownloading = new SemaphoreSlim(1);
            _reportBytesProgress = bytesReporter;
            _requestThrottler = requestThrottler;
            _pausedIdentifierBytes = GetEncodedBytes(AppConstants.DownloaderFileMagicString);

            PropertyChanged += propertyChangedEventHandler;
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
            this.TotalBytesToDownload = totalBytesToDownload;
            this.BytesDownloadedThisSession = 0;
            this.Speed = null;
            this.SupportsResume = false;
            this.DateCreated = DateTime.Now;
            this.NumberOfActiveStreams = null;
            this.StatusCode = null;
            this.TimeRemaining = null;
            this.IsQueued = false;

            if (enqueue)
            {
                this.Enqueue();
            }

            if (this.IsCompleted)
            {
                // if file exists and we know the final size, simply set finished instead of beginning a verification request
                SetFinished();
            }
            else
            {
                _semaphoreDownloading.Wait();
                Task.Run(async () => await VerifyDownloadAsync(url)).ContinueWith(t => _semaphoreDownloading.Release());
            }
        }
        #endregion // Constructors

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
            this.Progress = (int)((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100);
            this.Status = DownloadStatus.Paused;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
        }

        private void SetQueued()
        {
            this.IsQueued = true;
            RaisePropertyChanged(nameof(this.IsQueued));
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

        private async Task VerifyDownloadAsync(string url)
        {
            // verifies the download; uses RequestThrottler to throttle many requests
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
                // check if we've seen this recently and throttle requests
                RequestModel? requestModel = _requestThrottler.Has(url);
                if (requestModel != null)
                {
                    // we've seen this *recently*; just grab seen data
                    this.TotalBytesToDownload = requestModel?.TotalBytesToDownload;
                    this.StatusCode = requestModel?.Status;
                }
                else
                {
                    // we haven't seen this before; verify actual url
                    var downloadVerification = await VerifyUrlAsync(url);
                    this.StatusCode = downloadVerification.Status;
                    this.TotalBytesToDownload = downloadVerification.TotalBytesToDownload;
                    RaisePropertyChanged(nameof(this.TotalBytesToDownload));
                    if (this.TotalBytesToDownload > 0)
                    {
                        this.SupportsResume = true;
                        RaisePropertyChanged(nameof(this.SupportsResume));
                    }
                    switch (this.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.NotFound:
                            _requestThrottler.Keep(url, TotalBytesToDownload, StatusCode);
                            break;
                    }
                }

                if (this.StatusCode != HttpStatusCode.OK)
                {
                    // invalid url
                    SetErrored();
                }
                else
                {
                    // valid url
                    if (this.IsPaused)
                    {
                        // paused download
                        this.TotalBytesCompleted = GetCorrectFileLength(this.Destination, _pausedIdentifierBytes) ?? 0;
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
            _refreshCollectionDel?.Invoke();
        }

        private async Task<DownloadVerificationModel> VerifyUrlAsync(string url)
        {
            // force checks the url and returns TotalBytesToDownload
            DownloadVerificationModel downloadVerificationModel;
            downloadVerificationModel.Status = null;
            downloadVerificationModel.TotalBytesToDownload = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using (HttpResponseMessage response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    downloadVerificationModel.Status = response.StatusCode;
                    downloadVerificationModel.TotalBytesToDownload = response.Content.Headers.ContentLength;
                }
            }
            catch (HttpRequestException)
            {
                downloadVerificationModel.Status = HttpStatusCode.BadRequest;
            }
            catch
            {
                downloadVerificationModel.Status = null;
            }
            return downloadVerificationModel;
        }

        private long? GetCorrectFileLength(string destination, byte[] identifyingBytes)
        {
            if (File.Exists(destination))
            {
                long length = new FileInfo(destination).Length;
                if (IsFilePaused(destination, identifyingBytes))
                {
                    return length - identifyingBytes.Length;
                }
                else
                {
                    return length;
                }
            }
            return null;
        }

        private bool CreateEmptyDownload(string destination, byte[] identiferBytes)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(destination)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                }
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                using (var fs = File.Create(destination))
                {
                    using (var binaryWriter = new BinaryWriter(fs))
                    {
                        binaryWriter.Write(identiferBytes, 0, identiferBytes.Length);
                    }
                }
                // ensure this is an empty download by checking length
                if (File.Exists(destination)
                    && IsFilePaused(destination, identiferBytes)
                    && new FileInfo(destination).Length <= identiferBytes.Length)
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
                throw new AMDownloaderException("Failed to create new download: " + destination, ex);
            }
        }

        private byte[] GetEncodedBytes(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        private bool IsFilePaused(string destination, byte[] identifyingBytes)
        {
            if (!File.Exists(destination)) return false;
            try
            {
                using (FileStream fs = new FileStream(destination, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    var buffer = new byte[identifyingBytes.Length];
                    var read = reader.Read(buffer, 0, identifyingBytes.Length);
                    if (read > 0)
                    {
                        byte[] output = new byte[read];
                        buffer.ToList().CopyTo(0, output, 0, read);
                        return identifyingBytes.SequenceEqual(output);
                    }
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (Exception ex)
            {
                throw new AMDownloaderException(ex.Message, ex);
            }

            return true;
        }

        public async Task<bool> RemoveIdentifyingBytesFromFile(string destination, byte[] remove, int bufferLength = AppConstants.RemovingFileBytesBufferLength)
        {
            if (!File.Exists(destination)) return false;
            var tempFilePath = destination + ".tmp";
            using (var tempFile = File.Create(tempFilePath))
            using (var originalFile = new FileStream(destination, FileMode.Open, FileAccess.Read))
            {
                originalFile.Seek(remove.Length, SeekOrigin.Begin);
                await originalFile.CopyToAsync(tempFile, bufferLength);
            }
            File.Delete(destination);
            File.Move(tempFilePath, destination);
            return true;
        }

        private async Task<DownloadStatus> ProcessStreamsAsync(long bytesDownloadedPreviously)
        {
            var status = DownloadStatus.Error;
            List<HttpRequestMessage> requests = new List<HttpRequestMessage>();
            List<Task<bool>> streamTasks = new List<Task<bool>>();
            Task<bool[]> aggregatedStreamTasks;
            bool[] streamResults = null;
            long progressReportingFrequency = AppConstants.DownloaderStreamBufferLength;
            long nextProgressReportAt = AppConstants.DownloaderStreamBufferLength;
            long maxDownloadSpeed = Settings.Default.MaxDownloadSpeed * 1024;
            int numStreams = Settings.Default.MaxConnectionsPerDownload;
            SemaphoreSlim semaphoreProgress = new SemaphoreSlim(1);
            IProgress<int> streamProgress = new Progress<int>(async (value) =>
            {
                await semaphoreProgress.WaitAsync();
                this.BytesDownloadedThisSession += value;
                this.TotalBytesCompleted = this.BytesDownloadedThisSession + bytesDownloadedPreviously;
                _reportBytesProgress.Report(value);
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
            if (!this.SupportsResume || chunkSize <= (long)ByteConstants.MEGABYTE)
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
                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get,
                    Headers = { Range = new RangeHeaderValue(bytesDownloadedPreviously, TotalBytesToDownload) }
                };
                requests.Add(request);
            }

            if (this.SupportsResume)
            {
                if (this.TotalBytesToDownload <= (long)ByteConstants.KILOBYTE)
                {
                    progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 1;
                }
                else if (this.TotalBytesToDownload <= (long)ByteConstants.MEGABYTE)
                {
                    progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 10;
                }
                else
                {
                    progressReportingFrequency = (this.TotalBytesToDownload ?? 0) / 100;
                }

                if (progressReportingFrequency < AppConstants.DownloaderStreamBufferLength)
                    progressReportingFrequency = AppConstants.DownloaderStreamBufferLength;
                else if (progressReportingFrequency > 512 * (long)ByteConstants.KILOBYTE)
                    progressReportingFrequency = 512 * (long)ByteConstants.KILOBYTE;

                nextProgressReportAt = progressReportingFrequency;
            }

            // Set up the tasks to process the requests
            foreach (var request in requests)
            {
                streamTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (!Directory.Exists(Path.GetDirectoryName(this.Destination)))
                        {
                            throw new AMDownloaderException("Destination directory does not exist.");
                        }
                        if (!File.Exists(this.Destination))
                        {
                            throw new AMDownloaderException("A new download has not been created.");
                        }

                        using (var destinationStream = new FileStream(this.Destination, FileMode.Append, FileAccess.Write))
                        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                        using (var sourceStream = await response.Content.ReadAsStreamAsync())
                        using (var binaryWriter = new BinaryWriter(destinationStream))
                        {
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
                    }
                    catch (Exception ex)
                    {
                        throw new AMDownloaderException(ex.Message, ex);
                    }
                    finally
                    {
                        --this.NumberOfActiveStreams;
                        RaisePropertyChanged(nameof(this.NumberOfActiveStreams));
                    }
                }));

            }

            StartMeasuringSpeed();
            StartMeasuringEta();

            this.NumberOfActiveStreams = requests.Count;
            RaisePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Run the tasks
            aggregatedStreamTasks = Task.WhenAll(streamTasks.ToArray());
            try
            {
                streamResults = await aggregatedStreamTasks;
                // Operation complete; verify state
                var finished = true;
                foreach (var result in streamResults)
                {
                    if (!result)
                    {
                        finished = false;
                        break;
                    }
                }
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
                else
                {
                    if (_ctsPaused.IsCancellationRequested)
                    {
                        status = DownloadStatus.Paused;
                    }
                    else if (_ctsCanceled.IsCancellationRequested)
                    {
                        status = DownloadStatus.Ready;
                    }
                }
            }

            this.NumberOfActiveStreams = null;
            RaisePropertyChanged(nameof(this.NumberOfActiveStreams));

            // Update final size
            if (!this.SupportsResume) this.TotalBytesToDownload = this.TotalBytesCompleted;

            _ctsCanceled = null;
            _ctsPaused = null;
            _ctCancel = default;
            _ctPause = default;

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

                        double eta = ((this.TotalBytesToDownload ?? 0) - this.TotalBytesCompleted) * ((double)stopWatch.ElapsedMilliseconds / bytesCaptured);
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

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private string GetExtension()
        {
            if (File.Exists(this.Destination))
            {
                return Path.GetExtension(this.Destination);
            }
            return Path.GetExtension(this.Url);
        }
        #endregion // Private methods

        #region Public methods
        public void Enqueue()
        {
            if (this.IsQueued || this.IsBeingDownloaded) return;
            SetQueued();
            RaiseEvent(DownloadEnqueued);
            _refreshCollectionDel?.Invoke();
        }

        public void Dequeue()
        {
            if (!this.IsQueued || this.IsBeingDownloaded) return;
            SetDequeued();
            RaiseEvent(DownloadDequeued);
            _refreshCollectionDel?.Invoke();
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
                    DownloadVerificationModel downloadVerification;
                    downloadVerification = await VerifyUrlAsync(this.Url);
                    // Ensure url is valid for all downloads
                    if (downloadVerification.Status != HttpStatusCode.OK)
                    {
                        SetErrored();
                        return;
                    }

                    if (this.IsPaused)
                    {
                        // Download is paused; resume from specific point
                        bytesAlreadyDownloaded = GetCorrectFileLength(this.Destination, _pausedIdentifierBytes) ?? 0;
                    }
                    else
                    {
                        if (!CreateEmptyDownload(this.Destination, _pausedIdentifierBytes))
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
                    SetReady();
                    break;

                case (DownloadStatus.Paused):
                    this.Pause();
                    SetPaused();
                    break;

                case (DownloadStatus.Finished):
                    this.Status = DownloadStatus.Finishing;
                    RaisePropertyChanged(nameof(this.Status));
                    await RemoveIdentifyingBytesFromFile(this.Destination, _pausedIdentifierBytes);
                    SetFinished();
                    break;

                default:
                    this.Cancel();
                    SetReady();
                    break;
            }
            _semaphoreDownloading.Release();
            _refreshCollectionDel?.Invoke();
            RaiseEvent(DownloadStopped);
            if (_taskCompletion.Task.Result == DownloadStatus.Finished) RaiseEvent(DownloadFinished);
        }

        public void Pause()
        {
            /*if (this.IsCompleted || _taskCompletion == null)
            {
                // Nothing to pause
                return;
            }*/

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

        public async Task ForceReCheckAsync()
        {
            if (this.IsBeingDownloaded) return;
            await _semaphoreDownloading.WaitAsync();
            await VerifyDownloadAsync(this.Url);
            _semaphoreDownloading.Release();
        }
        #endregion // Public methods
    }
}
