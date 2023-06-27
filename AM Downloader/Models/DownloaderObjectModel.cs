// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using Polly;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Linq;

namespace AMDownloader.Models
{
    public enum DownloadStatus
    {
        Ready, Downloading, Paused, Finished, Errored
    }

    internal class DownloaderObjectModel : IQueueable, INotifyPropertyChanged
    {
        #region Fields

        private readonly HttpClient _httpClient;
        private readonly IProgress<long> _reportProgressBytes;
        private int _connections;
        private TaskCompletionSource _tcs;
        private CancellationTokenSource _ctsPause, _ctsCancel, _ctsLinked;
        private CancellationToken _ctPause, _ctCancel, _ctLinked;

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
        /// <summary>
        /// Gets the estimated time remaining (in milliseconds) to complete the download.
        /// </summary>
        public double? TimeRemaining { get; private set; }
        /// <summary>
        /// Gets the estimated speed of the download (in bytes/second).
        /// </summary>
        public long? Speed { get; private set; }
        public int Connections => _connections;
        /// <summary>
        /// Gets the maximum number of connections allowed for this download.
        /// Once the download has started, this value cannot be changed without
        /// canceling the download first.
        /// </summary>
        public int ConnLimit { get; private set; }
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
                Settings.Default.MaxParallelConnPerDownload,
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
            int connLimit,
            HttpStatusCode? httpStatusCode,
            DownloadStatus status,
            EventHandler downloadCreated,
            EventHandler downloadStarted,
            EventHandler downloadStopped,
            PropertyChangedEventHandler propertyChanged,
            IProgress<long> bytesReporter)
        {
            var destFileInfo = new FileInfo(destination);

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
            ConnLimit = connLimit;
            StatusCode = httpStatusCode;
            Status = status;

            // are we restoring an existing download?
            if ((IsPaused || IsErrored)
                && TempFilesExist()
                && !File.Exists(destination))
            {
                // paused or interrupted
                BytesDownloaded = GetTempFilesLength();
            }
            else if (IsCompleted
                && !TempFilesExist()
                && File.Exists(destination)
                && destFileInfo.Length == TotalBytesToDownload)
            {
                // finished
                BytesDownloaded = new FileInfo(destination).Length;
            }
            else
            {
                // new or errored download

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

                    // for new downloads, we can reset the number of conns allowed
                    ConnLimit = Settings.Default.MaxParallelConnPerDownload;

                    RaisePropertyChanged(nameof(ConnLimit));
                }

                await DownloadAsync();

                // update sizes to reflect actual size on disk
                BytesDownloaded = new FileInfo(Destination).Length;
                TotalBytesToDownload = BytesDownloaded;
                Status = DownloadStatus.Finished;

                RaisePropertyChanged(nameof(TotalBytesToDownload));
            }
            catch
            {
                if (TempFilesExist())
                {
                    BytesDownloaded = GetTempFilesLength();
                }
                else
                {
                    BytesDownloaded = 0;
                }

                if (_ctLinked.IsCancellationRequested)
                {
                    // interrupted by user
                    if (_ctPause.IsCancellationRequested && SupportsResume)
                    {
                        Status = DownloadStatus.Paused;
                    }
                    else
                    {
                        Status = DownloadStatus.Ready;
                    }
                }
                else
                {
                    // interrupted due an exception not related to user cancellation
                    // e.g. no connection, invalid url
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

            RaisePropertyChanged(nameof(BytesDownloaded));
            RaisePropertyChanged(nameof(Progress));
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

                    BytesDownloaded = 0;
                    Status = DownloadStatus.Ready;

                    RaisePropertyChanged(nameof(BytesDownloaded));
                    RaisePropertyChanged(nameof(Progress));
                    RaisePropertyChanged(nameof(Status));
                }
            }
            catch (ObjectDisposedException)
            {
                // cancel a download which entered
                // paused state after being started

                CleanupTempFiles();

                BytesDownloaded = 0;
                Status = DownloadStatus.Ready;

                RaisePropertyChanged(nameof(BytesDownloaded));
                RaisePropertyChanged(nameof(Progress));
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
            return GetTempFiles().Length > 0;
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

                    // ensure the url returns a valid http status code
                    StatusCode = response.StatusCode;
                    if (StatusCode != HttpStatusCode.OK && StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new AMDownloaderUrlException(Url);
                    }

                    // get the size of the download
                    TotalBytesToDownload = response.Content.Headers.ContentLength;
                    RaisePropertyChanged(nameof(TotalBytesToDownload));
                }

                StartMeasuringStats();

                // setup the connections

                List<Task> connTasks = new();
                int connCount = ConnLimit;

                // if the file doesn't support resume or is too small, open just 1 conn
                if (!SupportsResume || TotalBytesToDownload < connCount)
                {
                    if (!SupportsResume)
                    {
                        connCount = 1;
                    }
                    else if (TotalBytesToDownload < connCount)
                    {
                        connCount = (int)TotalBytesToDownload;
                    }

                    // fewer than the requested number of conns can be opened due
                    // to the nature of the url; update conn limit to reflect this
                    ConnLimit = connCount;
                    RaisePropertyChanged(nameof(ConnLimit));
                }

                Log.Debug($"\n{Name}: " +
                    $"Total = {TotalBytesToDownload}, " +
                    $"Remaining = {TotalBytesToDownload - BytesDownloaded}, " +
                    $"Conns = {connCount}");

                for (int i = 0; i < connCount; i++)
                {
                    // must be declared here to ensure var is captured correctly in the tasks
                    int conn = i;

                    var t = Task.Run(async () =>
                    {
                        var connFile = $"{Destination}.{conn}{Constants.TempDownloadExtension}";
                        long connLength = 0;

                        using var connRequest = new HttpRequestMessage()
                        {
                            RequestUri = new Uri(Url),
                            Method = HttpMethod.Get
                        };

                        if (SupportsResume)
                        {
                            var connFileInfo = new FileInfo(connFile);
                            long connStart = (TotalBytesToDownload ?? 0) / connCount * conn;
                            // if this is the last conn, read till the end of the file
                            long connEnd = conn == connCount - 1
                                ? (TotalBytesToDownload ?? 0)
                                : (TotalBytesToDownload ?? 0) / connCount * (conn + 1);

                            if (File.Exists(connFile))
                            {
                                if (connFileInfo.Length > 0)
                                {
                                    // if resuming a paused download, add the bytes already downloaded
                                    // which is determined from the length of the existing conn file
                                    connStart += connFileInfo.Length;
                                }
                            }

                            connLength = connEnd - connStart;

                            Log.Debug("{0,1}{1,2}{2,12}{3,12}{4,12}{5,12}{6,12}{7,12}",
                                "Conn = ",
                                conn,
                                "Start = ",
                                connStart,
                                "End = ",
                                connEnd,
                                "Length = ",
                                connLength);

                            // conn already completed its allocated bytes
                            if (connLength <= 0)
                            {
                                Log.Debug($"Conn {conn} already completed.");

                                return;
                            }

                            connRequest.Headers.Range = new RangeHeaderValue(connStart, connEnd - 1);
                        }
                        else
                        {
                            // if the download doesn't support resume,
                            // simply delete any existing conn file and
                            // start from the beginning
                            File.Delete(connFile);
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
                        var buffer = new byte[4096];

                        // vars for speed throttler
                        Stopwatch t_Stopwatch = new();
                        long connSpeedLimit = Settings.Default.MaxDownloadSpeed / connCount; // B/s
                        long t_BytesExpected = 0, t_BytesReceived = 0;
                        long t_TimeExpected = 0, t_TimeTaken = 0, t_Delay = 0; // ms

                        Log.Debug($"Conn {conn} speed limit = {connSpeedLimit}");

                        Interlocked.Increment(ref _connections);

                        // start downloading
                        while (true)
                        {
                            t_Stopwatch.Start();

                            read = await timeoutPolicy.ExecuteAsync(async ct =>
                            await readStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct), _ctLinked);

                            t_Stopwatch.Stop();

                            if (read == 0)
                            {
                                break;
                            }

                            readThisConn += read;

                            var data = new byte[read];

                            Array.Copy(buffer, 0, data, 0, read);
                            writer.Write(data, 0, read);

                            progressReporter.Report(read);

                            // reached the end of this conn's allocated bytes
                            if (SupportsResume && readThisConn >= connLength)
                            {
                                break;
                            }

                            // speed throttler

                            t_BytesReceived += read;
                            t_TimeTaken = t_Stopwatch.ElapsedMilliseconds;

                            if (connSpeedLimit > 0 && t_TimeTaken > 0)
                            {
                                t_BytesExpected = (long)((double)connSpeedLimit / 1000 * t_TimeTaken);
                                t_TimeExpected = (long)(1000 / (double)connSpeedLimit * t_BytesReceived);

                                if (t_BytesReceived > t_BytesExpected || t_TimeTaken < t_TimeExpected)
                                {
                                    t_Delay = t_TimeExpected - t_TimeTaken;

                                    if (t_Delay > 0)
                                    {
                                        Log.Debug($"Conn {conn} sleeping for {t_Delay} ms");

                                        await Task.Delay((int)t_Delay, _ctLinked);
                                    }

                                    t_BytesReceived = 0;
                                    t_Stopwatch.Reset();
                                }
                            }
                        }

                        Log.Debug($"Conn {conn} completed\t\tRead this session = {readThisConn}");

                        Interlocked.Decrement(ref _connections);
                    });

                    connTasks.Add(t);
                }

                await Task.WhenAll(connTasks);

                // download complete; merge temp files
                MergeFiles(GetTempFiles().Select(o => o.FullName), Destination);
            }
            catch (Exception ex)
            {
                Log.Error($"{ex.Message} ({Name})");

                if (!SupportsResume || BytesDownloaded == 0 || _ctCancel.IsCancellationRequested)
                {
                    // cleanup if download wasn't paused or can't be resumed
                    CleanupTempFiles();
                }

                throw new Exception(null, ex);
            }
        }

        private void StartMeasuringStats()
        {
            Task.Run(async () =>
            {
                long fromBytes, bytesCaptured;
                double timeRemaining;
                Stopwatch stopwatch = new();

                while (IsDownloading)
                {
                    stopwatch.Restart();
                    fromBytes = BytesDownloaded;

                    await Task.Delay(1000);

                    bytesCaptured = BytesDownloaded - fromBytes;
                    stopwatch.Stop();

                    Speed = (long)((double)bytesCaptured / stopwatch.ElapsedMilliseconds * 1000);
                    RaisePropertyChanged(nameof(Speed));

                    if (SupportsResume && bytesCaptured > 0)
                    {
                        timeRemaining = (double)((double)stopwatch.ElapsedMilliseconds
                            / bytesCaptured
                            * ((TotalBytesToDownload ?? 0) - BytesDownloaded));

                        if (timeRemaining > 0 && timeRemaining != TimeRemaining)
                        {
                            TimeRemaining = timeRemaining;
                            RaisePropertyChanged(nameof(TimeRemaining));
                        }

                        RaisePropertyChanged(nameof(Progress));
                    }

                    RaisePropertyChanged(nameof(BytesDownloaded));
                    RaisePropertyChanged(nameof(Connections));
                }

                TimeRemaining = null;
                Speed = null;
                _connections = 0;

                RaisePropertyChanged(nameof(TimeRemaining));
                RaisePropertyChanged(nameof(Speed));
                RaisePropertyChanged(nameof(Connections));
            });
        }

        /// <summary>
        /// Merges the <paramref name="sources"/> into the <paramref name="target"/>.
        /// If the <paramref name="target"/> already exists, it is overwritten.
        /// </summary>
        /// <param name="sources">The list of files to merge.</param>
        /// <param name="target">The output of the merging operation.</param>
        /// <param name="deleteSource">If <see langword="true"/>, the sources
        /// will be deleted once merged.</param>
        private void MergeFiles(IEnumerable<string> sources, string target, bool deleteSource = true)
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

                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var data = new byte[read];

                    Array.Copy(buffer, 0, data, 0, read);
                    writer.Write(data, 0, read);
                }

                reader.Dispose();
                readStream.Dispose();

                if (deleteSource)
                {
                    File.Delete(source);
                }
            }
        }

        private FileInfo[] GetTempFiles()
        {
            DirectoryInfo d = new(Path.GetDirectoryName(Destination));
            return d.GetFiles($"{Name}.*{Constants.TempDownloadExtension}");
        }

        /// <summary>
        /// Gets the sum of the lengths of all the temp files (in bytes).
        /// </summary>
        /// <returns></returns>
        private long GetTempFilesLength()
        {
            long length = 0;

            foreach (var f in GetTempFiles())
            {
                length += f.Length;
            }

            return length;
        }

        private void CleanupTempFiles()
        {
            if (TempFilesExist())
            {
                foreach (var f in GetTempFiles())
                {
                    f.Delete();
                }
            }
        }

        #endregion
    }
}