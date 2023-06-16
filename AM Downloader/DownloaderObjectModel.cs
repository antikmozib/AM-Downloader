// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using AMDownloader.ObjectModel;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using Polly;
using Polly.Timeout;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AMDownloader
{
    public enum DownloadStatus
    {
        Ready, Downloading, Paused, Finished, Errored
    }

    internal class DownloaderObjectModel : IQueueable, INotifyPropertyChanged
    {
        #region Fields

        /// <summary>
        /// Gets the interval between providing download progression updates.
        /// </summary>
        private const int _reportingDelay = 1000;
        private readonly HttpClient _httpClient;
        private readonly IProgress<long> _reportProgressBytes;
        private TaskCompletionSource _tcs;
        private CancellationTokenSource _ctsPause, _ctsCancel, _ctsLinked;
        private CancellationToken _ctPause, _ctCancel, _ctLinked;
        private readonly string _tempPath;

        #endregion

        #region Properties

        /// <summary>
        /// <see langword="true"/> if this download can be resumed after pausing.
        /// </summary>
        public bool SupportsResume => TotalBytesToDownload != null && TotalBytesToDownload > 0;
        public string Url { get; }
        public string Name => Path.GetFileName(Destination);
        /// <summary>
        /// Gets the full local path to the file.
        /// </summary>
        public string Destination { get; }
        public string TempDestination => _tempPath;
        public string Extension => Path.GetExtension(Destination);
        public DateTime DateCreated { get; }
        /// <summary>
        /// Gets the total number of bytes of the file.
        /// </summary>
        public long? TotalBytesToDownload { get; private set; }
        /// <summary>
        /// Gets the number of bytes of the file downloaded so far.
        /// </summary>
        public long BytesDownloaded { get; private set; }
        /// <summary>
        /// Gets the number of bytes of the file downloaded in the current session only.
        /// </summary>
        public long BytesDownloadedThisSession { get; private set; }
        public int Progress => SupportsResume
            ? (int)(BytesDownloaded / (double)TotalBytesToDownload * 100)
            : 0;
        public double? TimeRemaining { get; private set; }
        public long? Speed { get; private set; }
        public HttpStatusCode? StatusCode { get; private set; }
        public DownloadStatus Status { get; private set; }
        /// <summary>
        /// <see langword="true"/> if this download has not started yet.
        /// </summary>
        public bool IsReady => Status == DownloadStatus.Ready;
        public bool IsDownloading => Status == DownloadStatus.Downloading;
        public bool IsPaused => Status == DownloadStatus.Paused;
        public bool IsCompleted => Status == DownloadStatus.Finished;
        public bool IsErrored => Status == DownloadStatus.Errored;

        #endregion

        #region Events

        public event EventHandler DownloadCreated, DownloadStarted, DownloadStopped;
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Constructors

        public DownloaderObjectModel(
            HttpClient httpClient,
            string url,
            string destination,
            EventHandler downloadCreated,
            EventHandler downloadStarted,
            EventHandler downloadStopped,
            PropertyChangedEventHandler propertyChanged,
            IProgress<long> bytesReporter) : this(
                httpClient,
                url,
                destination,
                DateTime.Now,
                null,
                null,
                DownloadStatus.Ready,
                downloadCreated,
                downloadStarted,
                downloadStopped,
                propertyChanged,
                bytesReporter)
        { }

        public DownloaderObjectModel(
            HttpClient httpClient,
            string url,
            string destination,
            DateTime dateCreated,
            long? bytesToDownload,
            HttpStatusCode? httpStatusCode,
            DownloadStatus status,
            EventHandler downloadCreated,
            EventHandler downloadStarted,
            EventHandler downloadStopped,
            PropertyChangedEventHandler propertyChanged,
            IProgress<long> bytesReporter)
        {
            _httpClient = httpClient;
            _reportProgressBytes = bytesReporter;
            _tempPath = destination + Constants.DownloaderSplitedPartExtension;

            Url = url;
            Destination = destination;
            DateCreated = dateCreated;
            TotalBytesToDownload = bytesToDownload;
            BytesDownloaded = 0;
            BytesDownloadedThisSession = 0;
            TimeRemaining = null;
            Speed = null;
            StatusCode = httpStatusCode;
            Status = status;

            // are we restoring an existing download?
            if ((IsPaused || IsErrored) && File.Exists(_tempPath) && !File.Exists(destination))
            {
                // paused or interrupted
                BytesDownloaded = new FileInfo(_tempPath).Length;
            }
            else if (IsCompleted && !File.Exists(_tempPath) && File.Exists(destination))
            {
                // finished

                // set downloaded bytes to total bytes as the size is dictated by the file system
                BytesDownloaded = TotalBytesToDownload ?? new FileInfo(destination).Length;
            }
            else
            {
                // new or errored download

                BytesDownloaded = 0;

                // if we have any status other than Ready or Errored at this point
                // it means there has been an error while restoring, e.g. a Paused or
                // Finished status was requested but the required files weren't found
                if (!IsReady && !IsErrored)
                {
                    Status = DownloadStatus.Errored;
                }
            }

            DownloadCreated += downloadCreated;
            DownloadStarted += downloadStarted;
            DownloadStopped += downloadStopped;
            PropertyChanged += propertyChanged;

            RaiseEvent(DownloadCreated);
        }

        #endregion

        #region Public methods

        public async Task StartAsync()
        {
            if (IsDownloading || IsCompleted)
            {
                return;
            }

            _tcs = new TaskCompletionSource();
            _ctsPause = new CancellationTokenSource();
            _ctPause = _ctsPause.Token;
            _ctsCancel = new CancellationTokenSource();
            _ctCancel = _ctsCancel.Token;
            _ctsLinked = CancellationTokenSource.CreateLinkedTokenSource(_ctPause, _ctCancel);
            _ctLinked = _ctsLinked.Token;

            BytesDownloadedThisSession = 0;

            Status = DownloadStatus.Downloading;
            RaisePropertyChanged(nameof(Status));
            RaiseEvent(DownloadStarted);

            try
            {
                if (!SupportsResume || BytesDownloaded == 0)
                {
                    // creating a new download

                    Directory.CreateDirectory(Path.GetDirectoryName(_tempPath));

                    if (File.Exists(_tempPath))
                    {
                        File.Delete(_tempPath);
                    }
                }
                else
                {
                    // resuming a paused download

                    // re-read the temp file size because this is the
                    // actual point we'll be resuming from as the size
                    // is dictated by the file system
                    BytesDownloaded = new FileInfo(_tempPath).Length;
                }

                await DownloadAsync();

                File.Move(_tempPath, Destination, true);

                TotalBytesToDownload = BytesDownloaded;
                Status = DownloadStatus.Finished;

                RaisePropertyChanged(nameof(TotalBytesToDownload));
                RaisePropertyChanged(nameof(Progress));
            }
            catch (Exception ex)
            when (ex is OperationCanceledException
                || ex is AMDownloaderUrlException
                || ex is HttpRequestException
                || ex is TimeoutRejectedException
                || ex is IOException)
            {
                if (_ctLinked.IsCancellationRequested)
                {
                    // download was paused or canceled by user

                    if (_ctPause.IsCancellationRequested && SupportsResume)
                    {
                        Status = DownloadStatus.Paused;
                    }
                    else
                    {
                        CleanupTempDownload();

                        Status = DownloadStatus.Ready;
                        RaisePropertyChanged(nameof(BytesDownloaded));
                        RaisePropertyChanged(nameof(Progress));
                    }
                }
                else
                {
                    // download interrupted by an exception not related to cancellation
                    // e.g. timeout, invalid url, IO exception

                    if (!SupportsResume)
                    {
                        CleanupTempDownload();
                    }

                    /*if (ex is OperationCanceledException && BytesDownloaded == 0)
                    {
                        // no network connection
                        Status = DownloadStatus.Ready;
                    }
                    else
                    {
                        Status = DownloadStatus.Errored;
                    }*/

                    Status = DownloadStatus.Errored;
                }
            }

            _ctsLinked.Dispose();
            _ctsPause.Dispose();
            _ctsCancel.Dispose();
            _ctLinked = default;
            _ctPause = default;
            _ctCancel = default;
            _tcs.SetResult();

            RaisePropertyChanged(nameof(Status));
            RaiseEvent(DownloadStopped);
        }

        public void Pause()
        {
            try
            {
                _ctsPause?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // not downloading
            }
        }

        public async Task PauseAsync()
        {
            Pause();

            if (_tcs != null)
            {
                await _tcs.Task;
            }
        }

        public void Cancel()
        {
            try
            {
                if (_ctsCancel != null)
                {
                    _ctsCancel.Cancel();
                }
                else
                {
                    // cancel a download which entered
                    // paused state after being restored

                    CleanupTempDownload();

                    Status = DownloadStatus.Ready;
                    RaisePropertyChanged(nameof(Status));
                }
            }
            catch (ObjectDisposedException)
            {
                // cancel a download which entered
                // paused state after being started

                CleanupTempDownload();

                Status = DownloadStatus.Ready;
                RaisePropertyChanged(nameof(Status));
            }
        }

        public async Task CancelAsync()
        {
            Cancel();

            if (_tcs != null)
            {
                await _tcs.Task;
            }
        }

        #endregion

        #region Private methods

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }

        private async Task DownloadAsync()
        {
            HttpRequestMessage request;
            IAsyncPolicy timeoutPolicy = Policy.TimeoutAsync(_httpClient.Timeout);
            // reports the lifetime number of bytes
            IProgress<int> progressReporter = new Progress<int>((value) =>
            {
                BytesDownloaded += value;
                BytesDownloadedThisSession += value;
                _reportProgressBytes.Report(value);
            });

            if (SupportsResume && BytesDownloaded > 0)
            {
                // resuming an existing download
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(Url),
                    Method = HttpMethod.Get,
                    Headers = { Range = new RangeHeaderValue(BytesDownloaded, TotalBytesToDownload) }
                };
            }
            else
            {
                // creating a new download
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(Url),
                    Method = HttpMethod.Get
                };
            }

            try
            {
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _ctLinked);

                // check the validity of the url
                StatusCode = response.StatusCode;
                if (StatusCode != HttpStatusCode.OK && StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new AMDownloaderUrlException(Url);
                }

                // get the size of the download
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength != null && contentLength > 0)
                {
                    TotalBytesToDownload = BytesDownloaded + contentLength;
                    RaisePropertyChanged(nameof(TotalBytesToDownload));
                }

                StartMeasuringSpeed();
                StartMeasuringEta();

                // start downloading
                using var fileStream = new FileStream(_tempPath, FileMode.Append, FileAccess.Write);
                using var readStream = await response.Content.ReadAsStreamAsync(_ctLinked);
                using var writeStream = new BinaryWriter(fileStream);

                byte[] buffer = new byte[4096]; // 4 KB buffer size
                int read;
                int bytesReceived = 0;

                Stopwatch stopWatch = new();
                long maxDownloadSpeed = Settings.Default.MaxDownloadSpeed;
                int throttlerBytesReceived = 0, throttlerBytesExpected = 0;

                do
                {
                    stopWatch.Start();

                    read = await timeoutPolicy.ExecuteAsync(async ct => await readStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct), _ctLinked);

                    stopWatch.Stop();

                    byte[] data = new byte[read];

                    Array.Copy(buffer, 0, data, 0, read);
                    writeStream.Write(data, 0, data.Length);
                    bytesReceived += read;

                    progressReporter.Report(data.Length);

                    // Speed throttler

                    throttlerBytesReceived += read;

                    if (maxDownloadSpeed > 0 && stopWatch.ElapsedMilliseconds > 0)
                    {
                        throttlerBytesExpected = (int)((double)maxDownloadSpeed / 1000 * stopWatch.ElapsedMilliseconds);
                        long millisecondsExpected = (long)(1000 / (double)maxDownloadSpeed * throttlerBytesReceived);

                        if (throttlerBytesReceived > throttlerBytesExpected || stopWatch.ElapsedMilliseconds < millisecondsExpected)
                        {
                            long delay = millisecondsExpected - stopWatch.ElapsedMilliseconds;

                            if (delay > 0)
                            {
                                await Task.Delay((int)delay, _ctLinked);
                            }

                            throttlerBytesReceived = 0;
                            stopWatch.Reset();
                        }
                    }
                } while (read > 0);
            }
            catch (OperationCanceledException)
            {
                if (_ctLinked.IsCancellationRequested)
                {
                    // paused or canceled
                    _ctLinked.ThrowIfCancellationRequested();
                }
                else
                {
                    // timed out
                    throw new OperationCanceledException();
                }
            }
        }

        private void StartMeasuringSpeed()
        {
            long fromBytes;
            long toBytes;
            long bytesCaptured;

            Task.Run(async () =>
            {
                while (IsDownloading)
                {
                    fromBytes = BytesDownloaded;
                    await Task.Delay(_reportingDelay);
                    toBytes = BytesDownloaded;
                    bytesCaptured = toBytes - fromBytes;

                    if (bytesCaptured >= 0)
                    {
                        Speed = (long)((double)bytesCaptured / _reportingDelay * 1000);
                        RaisePropertyChanged(nameof(Speed));
                    }

                    RaisePropertyChanged(nameof(BytesDownloaded));

                    if (SupportsResume)
                    {
                        RaisePropertyChanged(nameof(Progress));
                    }
                }

                Speed = null;
                RaisePropertyChanged(nameof(Speed));
            });
        }

        private void StartMeasuringEta()
        {
            if (!SupportsResume)
            {
                TimeRemaining = null;
                RaisePropertyChanged(nameof(TimeRemaining));
            }
            else
            {
                Stopwatch stopWatch = new();

                Task.Run(async () =>
                {
                    long fromBytes;
                    long toBytes;
                    long bytesCaptured;

                    while (IsDownloading)
                    {
                        fromBytes = BytesDownloaded;
                        stopWatch.Restart();
                        await Task.Delay(_reportingDelay);
                        toBytes = BytesDownloaded;
                        bytesCaptured = toBytes - fromBytes;
                        stopWatch.Stop();

                        double timeRemaining = (double)(
                        stopWatch.ElapsedMilliseconds
                            / (double)bytesCaptured
                            * ((TotalBytesToDownload ?? 0) - BytesDownloaded));

                        if (timeRemaining >= 0 && timeRemaining != TimeRemaining)
                        {
                            TimeRemaining = timeRemaining;
                            RaisePropertyChanged(nameof(TimeRemaining));
                        }
                    }

                    TimeRemaining = null;
                    RaisePropertyChanged(nameof(TimeRemaining));
                });
            }
        }

        private void CleanupTempDownload()
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
                BytesDownloaded = 0;
            }
        }

        #endregion
    }
}