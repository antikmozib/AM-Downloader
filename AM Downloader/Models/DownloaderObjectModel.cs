// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using Polly;
using Polly.Timeout;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Intrinsics.Arm;
using System.Threading;
using System.Threading.Tasks;

namespace AMDownloader.Models
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
        private int _connections;
        private TaskCompletionSource _tcs;
        private CancellationTokenSource _ctsPause, _ctsCancel, _ctsLinked;
        private CancellationToken _ctPause, _ctCancel, _ctLinked;
        //private readonly string _tempPath;

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
        //public string TempDestination => _tempPath;
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
        public int? Connections => _connections == 0 ? null : _connections;
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
            _connections = 0;

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
            if ((IsPaused || IsErrored) && TempFilesExist() && !File.Exists(destination))
            {
                // paused or interrupted
                BytesDownloaded = GetTempFilesLength();
            }
            else if (IsCompleted && !TempFilesExist() && File.Exists(destination))
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

                    Directory.CreateDirectory(Path.GetDirectoryName(Destination));

                    if (TempFilesExist())
                    {
                        CleanupTempFiles();
                    }
                }
                else
                {
                    // resuming a paused download

                    // re-read the temp file size because this is the
                    // actual point we'll be resuming from as the size
                    // is dictated by the file system
                    BytesDownloaded = GetTempFilesLength();
                }

                await DownloadAsync();

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
                    // interrupted by user

                    if (_ctPause.IsCancellationRequested && SupportsResume)
                    {
                        Status = DownloadStatus.Paused;
                    }
                    else
                    {
                        CleanupTempFiles();

                        Status = DownloadStatus.Ready;
                        RaisePropertyChanged(nameof(BytesDownloaded));
                        RaisePropertyChanged(nameof(Progress));
                    }
                }
                else
                {
                    // interrupted due an exception not related to user cancellation
                    // e.g. no connection, invalid url

                    if (!SupportsResume)
                    {
                        CleanupTempFiles();
                    }

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

                    CleanupTempFiles();

                    Status = DownloadStatus.Ready;
                    RaisePropertyChanged(nameof(Status));
                }
            }
            catch (ObjectDisposedException)
            {
                // cancel a download which entered
                // paused state after being started

                CleanupTempFiles();

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

        public bool TempFilesExist()
        {
            DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(Destination));
            FileInfo[] files = d.GetFiles($"{Name}.*{Constants.DownloaderSplitedPartExtension}");

            return files.Length > 0;
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
            const int bufferLength = 4096;
            IAsyncPolicy timeoutPolicy = Policy.TimeoutAsync(_httpClient.Timeout);
            // reports the lifetime number of bytes
            IProgress<int> progressReporter = new Progress<int>((value) =>
            {
                BytesDownloaded += value;
                BytesDownloadedThisSession += value;
                _reportProgressBytes.Report(value);
            });

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Url)))
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
                        TotalBytesToDownload = contentLength;
                        RaisePropertyChanged(nameof(TotalBytesToDownload));
                    }
                }

                StartMeasuringSpeed();
                StartMeasuringEta();

                // setup the connections

                List<Task> connTasks = new();
                int connCount = Settings.Default.MaxParallelConnPerDownload;
                long toReadPerConn = (TotalBytesToDownload ?? 0) / connCount;

                // if the file doesn't support resume or is too small, open just 1 conn
                if (!SupportsResume || TotalBytesToDownload < bufferLength)
                {
                    connCount = 1;
                }

                Debug.WriteLine($"\nCycles = {connCount}, Length/cycle = {toReadPerConn}");

                for (int i = 0; i < connCount; i++)
                {
                    // must be declared here to ensure var is captured correctly in the tasks
                    int cycle = i;

                    var t = Task.Run(async () =>
                    {
                        var connFile = $"{Destination}.{cycle}{Constants.DownloaderSplitedPartExtension}";
                        var connFileInfo = new FileInfo(connFile);

                        long connByteStart = (toReadPerConn + 1) * cycle;
                        long connByteEnd = cycle == connCount - 1 ? (TotalBytesToDownload ?? 0) : connByteStart + toReadPerConn;
                        long connByteLength;

                        if (File.Exists(connFile))
                        {
                            if (SupportsResume && connFileInfo.Length > 0)
                            {
                                connByteStart = (cycle * (toReadPerConn + 1)) + connFileInfo.Length;
                            }
                            else
                            {
                                File.Delete(connFile);
                            }
                        }

                        connByteLength = connByteEnd - connByteStart;

                        Debug.WriteLine($"#{cycle}:\t\tCycle byte start = {connByteStart}\t\tCycle byte end = {connByteEnd}\t\tCycle byte length = {connByteLength}");

                        if (connByteLength <= 0)
                        {
                            return;
                        }

                        using var connRequest = new HttpRequestMessage()
                        {
                            RequestUri = new Uri(Url),
                            Method = HttpMethod.Get
                        };

                        if (SupportsResume)
                        {
                            connRequest.Headers.Range = new RangeHeaderValue(connByteStart, connByteEnd);
                        }

                        using var connResponse = await _httpClient.SendAsync(
                            connRequest,
                            HttpCompletionOption.ResponseHeadersRead,
                            _ctLinked);

                        using var readStream = await connResponse.Content.ReadAsStreamAsync(_ctLinked);

                        using var writeStream = new FileStream(
                            connFile,
                            FileMode.Append,
                            FileAccess.Write);

                        using var writer = new BinaryWriter(writeStream);

                        long readThisConn = 0;
                        int read = 0;
                        var buffer = new byte[bufferLength];

                        Interlocked.Increment(ref _connections);

                        // start downloading

                        do
                        {
                            read = await timeoutPolicy.ExecuteAsync(async ct =>
                                await readStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct), _ctLinked);
                            readThisConn += read;

                            var data = new byte[read];

                            Array.Copy(buffer, 0, data, 0, read);
                            writer.Write(data, 0, read);

                            progressReporter.Report(read);

                            // reached the end of this conn's length
                            if (SupportsResume && readThisConn > connByteLength)
                            {
                                break;
                            }
                        } while (read > 0);

                        Interlocked.Decrement(ref _connections);
                    });

                    connTasks.Add(t);
                }

                await Task.WhenAll(connTasks);

                // download complete; merge temp files

                List<string> tempFiles = new();

                for (int i = 0; i < connCount; i++)
                {
                    tempFiles.Add($"{Destination}.{i}{Constants.DownloaderSplitedPartExtension}");
                }

                MergeFiles(tempFiles, Destination);
            }
            catch (OperationCanceledException)
            {
                if (!SupportsResume || BytesDownloaded == 0 || _ctCancel.IsCancellationRequested)
                {
                    // cleanup if download wasn't paused or can't be resumed
                    CleanupTempFiles();
                }

                throw new OperationCanceledException();
            }
            catch
            {

            }
        }

        /// <summary>
        /// Merges the <paramref name="sources"/> into the <paramref name="target"/>.
        /// If the <paramref name="target"/> already exists, it is overwritten.
        /// </summary>
        /// <param name="sources">The list of files to merge.</param>
        /// <param name="target">The output of the merging operation.</param>
        /// <param name="deleteSource">If <see langword="true"/>, the sources
        /// will be deleted once merged.</param>
        private void MergeFiles(List<string> sources, string target, bool deleteSource = true)
        {
            using var writeStream = new FileStream(target, FileMode.OpenOrCreate, FileAccess.Write);
            using var writer = new BinaryWriter(writeStream);

            foreach (var source in sources)
            {
                if (!File.Exists(source))
                {
                    continue;
                }

                var readStream = new FileStream(source, FileMode.Open);
                var reader = new BinaryReader(readStream);

                int read;
                var buffer = new byte[4096];

                do
                {
                    read = reader.Read(buffer, 0, buffer.Length);

                    var data = new byte[read];

                    Array.Copy(buffer, 0, data, 0, read);
                    writer.Write(data, 0, read);
                } while (read > 0);

                reader.Dispose();
                readStream.Dispose();

                if (deleteSource)
                {
                    File.Delete(source);
                }
            }
        }

        private long GetTempFilesLength()
        {
            long length = 0;

            DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(Destination));
            FileInfo[] files = d.GetFiles($"{Name}.*{Constants.DownloaderSplitedPartExtension}");

            foreach (var f in files)
            {
                length += f.Length;
            }

            return length;
        }

        private void CleanupTempFiles()
        {
            if (TempFilesExist())
            {
                DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(Destination));
                FileInfo[] files = d.GetFiles($"{Name}.*{Constants.DownloaderSplitedPartExtension}");

                foreach (var f in files)
                {
                    f.Delete();
                }

                BytesDownloaded = 0;
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
                    RaisePropertyChanged(nameof(Connections));

                    if (SupportsResume)
                    {
                        RaisePropertyChanged(nameof(Progress));
                    }
                }

                _connections = 0;

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

        #endregion
    }
}