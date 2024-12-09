// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Linq;
using Polly.Timeout;

namespace AMDownloader.Models
{
    public enum DownloadStatus
    {
        Ready, Downloading, Paused, Completed, Errored
    }

    public class DownloaderObjectModel : IQueueable, INotifyPropertyChanged
    {
        #region Fields

        private const int BufferLength = 4096;

        /// <summary>
        /// Number of attempts to make to get a response from the client for a connection in case of failures.
        /// </summary>
        private const int ConnFailureMaxRetryAttempts = 3;

        /// <summary>
        /// The delay between establishing consecutive connections or making retry attempts.
        /// </summary>
        private const int ConnAttemptDelay = 250;

        private readonly HttpClient _httpClient;

        private readonly IProgress<long> _reportProgressBytes;

        private int _connections;

        private TaskCompletionSource _tcs;

        private CancellationTokenSource _ctsPause, _ctsCancel, _ctsLinked;

        private CancellationToken _ctPause, _ctCancel, _ctLinked;

        private readonly bool _overwrite;

        #endregion

        #region Properties

        public string Id { get; }

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

        public DateTime CreatedOn { get; }

        public DateTime? CompletedOn { get; private set; }

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
        /// Gets the estimated time remaining, in milliseconds, to complete the download.
        /// </summary>
        public double? TimeRemaining { get; private set; }

        /// <summary>
        /// Gets the estimated speed of the download, in bytes/second.
        /// </summary>
        public long? Speed { get; private set; }

        public int Connections => _connections;

        /// <summary>
        /// Gets the maximum number of connections allowed for this download. Once the download has started, this value 
        /// cannot be changed without canceling the download first.
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

        public bool IsCompleted => Status == DownloadStatus.Completed;

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
            bool overwrite,
            EventHandler downloadCreated,
            EventHandler downloadStarted,
            EventHandler downloadStopped,
            PropertyChangedEventHandler propertyChanged,
            IProgress<long> bytesReporter) : this(
                httpClient: httpClient,
                id: Guid.NewGuid().ToString(),
                url: url,
                destination: destination,
                overwrite: overwrite,
                createdOn: DateTime.Now,
                completedOn: null,
                bytesToDownload: null,
                connLimit: Settings.Default.MaxParallelConnPerDownload,
                httpStatusCode: null,
                status: DownloadStatus.Ready,
                downloadCreated: downloadCreated,
                downloadStarted: downloadStarted,
                downloadStopped: downloadStopped,
                propertyChanged: propertyChanged,
                bytesReporter: bytesReporter)
        { }

        public DownloaderObjectModel(
            HttpClient httpClient,
            string id,
            string url,
            string destination,
            bool overwrite,
            DateTime createdOn,
            DateTime? completedOn,
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
            _overwrite = overwrite;
            Id = id;
            Url = url;
            Destination = destination;
            CreatedOn = createdOn;
            CompletedOn = completedOn;
            TotalBytesToDownload = bytesToDownload;
            BytesDownloaded = 0;
            BytesDownloadedThisSession = 0;
            TimeRemaining = null;
            Speed = null;
            ConnLimit = connLimit;
            StatusCode = httpStatusCode;
            Status = status;

            // Are we restoring an existing download?
            if ((IsPaused || IsErrored)
                && TempFilesExist()
                && !File.Exists(destination))
            {
                // Paused or interrupted.
                BytesDownloaded = GetTempFilesLength();
            }
            else if (IsCompleted
                && !TempFilesExist()
                && File.Exists(destination)
                && destFileInfo.Length == TotalBytesToDownload)
            {
                // Finished.
                BytesDownloaded = new FileInfo(destination).Length;
            }
            else
            {
                // New or errored download.

                // If we have any status other than Ready or Errored at this point it means there has been an error
                // while restoring, e.g. a Paused or Finished status was requested but the required files weren't found.
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

            Log.Debug($"{Id}: Created, Status = {Status}");
        }

        #endregion

        #region Public methods

        public override string ToString()
        {
            return Name;
        }

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
                    // Creating a new download.

                    Directory.CreateDirectory(Path.GetDirectoryName(Destination));

                    // If we've been instructed to overwrite existing files, do it.
                    if (_overwrite && File.Exists(Destination))
                    {
                        File.Delete(Destination);
                    }

                    // For new downloads, we can reset the number of connections allowed.
                    ConnLimit = Settings.Default.MaxParallelConnPerDownload;
                    RaisePropertyChanged(nameof(ConnLimit));
                }

                await DownloadAsync();

                CompletedOn = DateTime.Now;

                // Update sizes to reflect actual size on disk.
                BytesDownloaded = new FileInfo(Destination).Length;
                TotalBytesToDownload = BytesDownloaded;

                Status = DownloadStatus.Completed;

                RaisePropertyChanged(nameof(CompletedOn));
                RaisePropertyChanged(nameof(TotalBytesToDownload));
            }
            catch (Exception ex)
            {
                if (Directory.Exists(Path.GetDirectoryName(Destination)))
                {
                    if (TempFilesExist())
                    {
                        BytesDownloaded = GetTempFilesLength();
                    }
                    else
                    {
                        BytesDownloaded = 0;
                    }
                }

                if (_ctLinked.IsCancellationRequested)
                {
                    // Interrupted by user; must check for BytesDownloaded > 0 as we might reach a paused state even
                    // without downloading anything.
                    if (_ctPause.IsCancellationRequested && SupportsResume && BytesDownloaded > 0)
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
                    // Interrupted due an exception not related to user cancellation e.g. no connection, invalid url.
                    Status = DownloadStatus.Errored;
                    if (ex is AMDownloaderUrlException || ex.InnerException is AMDownloaderUrlException)
                    {
                        Log.Debug($"{Id}: {ex.Message}");
                    }
                    else
                    {
                        Log.Error(ex, ex.Message);
                    }
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

            Log.Debug($"{Id}: Processed, Status = {Status}");
        }

        public void Pause()
        {
            try
            {
                _ctsPause?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Not downloading.
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
            var cleanup = false;
            try
            {
                if (_ctsCancel != null)
                {
                    _ctsCancel.Cancel();
                }
                else
                {
                    // Cancel a download which entered paused state after being restored.
                    cleanup = true;
                }
            }
            catch (ObjectDisposedException)
            {
                // Cancel a download which entered paused state after being started.
                cleanup = true;
            }

            if (cleanup)
            {
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
            return GetTempFiles().Count > 0;
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
            var timeoutPolicy = Policy.TimeoutAsync(_httpClient.Timeout);

            // Reports the lifetime number of bytes.
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

                    // Ensure the URL returns a valid HTTP status code.
                    StatusCode = response.StatusCode;
                    if (StatusCode != HttpStatusCode.OK && StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new AMDownloaderUrlException($"The URL returned an invalid HttpStatusCode. ({StatusCode})");
                    }

                    // Get the size of the download
                    TotalBytesToDownload = response.Content.Headers.ContentLength;
                    RaisePropertyChanged(nameof(TotalBytesToDownload));
                }

                StartMeasuringStats();

                // Setup the connections.

                var connTasks = new List<Task>();
                var totalConnCount = ConnLimit;

                // If the file doesn't support resume or is too small, override ConnLimit.
                if (!SupportsResume)
                {
                    totalConnCount = 1;
                }
                else if (TotalBytesToDownload < (BufferLength * totalConnCount))
                {
                    totalConnCount = Math.Max((int)(TotalBytesToDownload / (double)BufferLength), 1);
                }

                if (totalConnCount != ConnLimit)
                {
                    // If ConnLimit was overridden, update ConnLimit to reflect this.
                    ConnLimit = totalConnCount;
                    RaisePropertyChanged(nameof(ConnLimit));
                }

                Log.Debug(
                    $"{Id}: "
                    + $"Total = {TotalBytesToDownload}, "
                    + $"Remaining = {TotalBytesToDownload - BytesDownloaded}, "
                    + $"Connections = {totalConnCount}");

                for (var i = 0; i < totalConnCount; i++)
                {
                    var currentConnNum = i; // Must be declared here to ensure variable is captured correctly in the tasks.
                    var t = Task.Run(async () =>
                    {
                        while (true)
                        {
                            var connFile = $"{Destination}.{currentConnNum}{Constants.TempDownloadExtension}";
                            var connFileInfo = new FileInfo(connFile);
                            var connStartPos = (TotalBytesToDownload ?? 0) / totalConnCount * currentConnNum;

                            // If this is the last connection, read till the end of the file.
                            var connEndPos = currentConnNum == totalConnCount - 1
                                ? (TotalBytesToDownload ?? 0)
                                : (TotalBytesToDownload ?? 0) / totalConnCount * (currentConnNum + 1);

                            long connLength = 0;
                            var connFailureRetryAttempt = 0;
                            if (SupportsResume)
                            {
                                if (File.Exists(connFile))
                                {
                                    if (connFileInfo.Length > 0)
                                    {
                                        // If resuming a paused download, add the bytes already downloaded which is
                                        // determined from the length of the existing conn file.
                                        connStartPos += connFileInfo.Length;
                                    }
                                }

                                connLength = connEndPos - connStartPos;

                                /*Log.Debug(
                                    "{0,1}{1,2}{2,12}{3,12}{4,12}{5,12}{6,12}{7,12}",
                                    "Connection = ", connId,
                                    "Start = ", connStartPos,
                                    "End = ", connEndPos,
                                    "Length = ", connTotalLength);*/

                                // Connection already completed its allocated bytes.
                                if (connLength <= 0)
                                {
                                    Log.Debug($"{Id}: Connection {currentConnNum} already completed.");

                                    return;
                                }
                            }
                            else
                            {
                                // If the download doesn't support resume, simply delete any existing conn file and start
                                // from the beginning.
                                File.Delete(connFile);
                            }

                            // Wait for attempting to establish another connection for the same file.
                            await Task.Delay(currentConnNum * ConnAttemptDelay);

                            try
                            {
                                using var connRequestMsg = new HttpRequestMessage(HttpMethod.Get, Url);
                                if (SupportsResume)
                                {
                                    connRequestMsg.Headers.Range = new RangeHeaderValue(connStartPos, connEndPos - 1);
                                }

                                using var connResponseMsg = await _httpClient.SendAsync(connRequestMsg, HttpCompletionOption.ResponseHeadersRead, _ctLinked);
                                if (connResponseMsg.StatusCode != HttpStatusCode.PartialContent)
                                {
                                    throw new AMDownloaderException($"The URL response returned an invalid HttpStatusCode. ({connResponseMsg.StatusCode})");
                                }
                                using var readStream = await connResponseMsg.Content.ReadAsStreamAsync(_ctLinked);
                                using var writeStream = new FileStream(
                                    connFile,
                                    FileMode.Append,
                                    FileAccess.Write);

                                using var writer = new BinaryWriter(writeStream);
                                long readConnLength = 0;
                                var read = 0;
                                var buffer = new byte[BufferLength];

                                // Variables for speed throttler.
                                var t_Stopwatch = new Stopwatch();
                                var connSpeedLimit = Settings.Default.MaxDownloadSpeed / totalConnCount; // B/s.
                                long t_BytesExpected = 0;
                                long t_BytesReceived = 0;
                                long t_TimeExpected = 0;
                                long t_TimeTaken = 0;
                                long t_Delay = 0; // ms.

                                //Log.Debug($"Connection {connId} speed limit = {connSpeedLimit}");

                                Log.Debug($"{Id}: Connection {currentConnNum} starting, To read this session = {connLength}");

                                Interlocked.Increment(ref _connections);

                                // Start downloading.
                                while (true)
                                {
                                    t_Stopwatch.Start();
                                    read = await timeoutPolicy.ExecuteAsync(async ct => await readStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct), _ctLinked);
                                    t_Stopwatch.Stop();

                                    if (read == 0)
                                    {
                                        break;
                                    }

                                    readConnLength += read;
                                    var data = new byte[read];
                                    Array.Copy(buffer, 0, data, 0, read);
                                    writer.Write(data, 0, read);
                                    progressReporter.Report(read);

                                    // Reached the end of this connection's allocated bytes.
                                    if (SupportsResume && readConnLength >= connLength)
                                    {
                                        break;
                                    }

                                    // Speed throttler.

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
                                                //Log.Debug($"Connection {connId} sleeping for {t_Delay} ms");

                                                await Task.Delay((int)t_Delay, _ctLinked);
                                            }

                                            t_BytesReceived = 0;
                                            t_Stopwatch.Reset();
                                        }
                                    }
                                }

                                Interlocked.Decrement(ref _connections);

                                Log.Debug($"{Id}: Connection {currentConnNum} completed, Read this session = {readConnLength}");

                                break;
                            }
                            catch (Exception ex)
                            {
                                if (ex is HttpRequestException
                                    || ex is TimeoutRejectedException
                                    || ex?.InnerException is TimeoutException
                                    || ex?.InnerException is SocketException sockEx && sockEx.ErrorCode == (int)SocketError.ConnectionReset)
                                {
                                    // Make the specified number of attempts to establish a connection in case of failures.
                                    if (connFailureRetryAttempt++ < ConnFailureMaxRetryAttempts)
                                    {
                                        Log.Debug(
                                            $"{Id}: {ex.GetType().Name} occurred for connection {currentConnNum}. "
                                            + $"Retrying (attempt {connFailureRetryAttempt} of {ConnFailureMaxRetryAttempts})...");

                                        await Task.Delay(connFailureRetryAttempt * ConnAttemptDelay);

                                        continue;
                                    }
                                }

                                throw;
                            }
                        }
                    });

                    connTasks.Add(t);
                }

                await Task.WhenAll(connTasks);

                // Download complete; merge temp files.
                MergeFiles(GetTempFiles().Select(o => o.FullName), Destination);
            }
            catch
            {
                if (!SupportsResume || BytesDownloaded == 0 || _ctCancel.IsCancellationRequested)
                {
                    // Cleanup if download wasn't paused or can't be resumed.
                    CleanupTempFiles();
                }

                throw;
            }
        }

        private void StartMeasuringStats()
        {
            Task.Run(async () =>
            {
                long fromBytes;
                long bytesCaptured;
                double timeRemaining;
                var stopwatch = new Stopwatch();
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
        /// Merges the <paramref name="sources"/> into the <paramref name="target"/>. If the <paramref name="target"/> 
        /// already exists, it is overwritten.
        /// </summary>
        /// <param name="sources">The list of files to merge.</param>
        /// <param name="target">The output file of the merging operation.</param>
        /// <param name="deleteSource">If <see langword="true"/>, the sources will be deleted once merged.</param>
        private static void MergeFiles(IEnumerable<string> sources, string target, bool deleteSource = true)
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
                var buffer = new byte[BufferLength];
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

        private List<FileInfo> GetTempFiles()
        {
            var files = new List<FileInfo>();
            for (var i = 0; i < ConnLimit; i++)
            {
                var tempFile = $"{Destination}.{i}{Constants.TempDownloadExtension}";
                if (File.Exists(tempFile))
                {
                    files.Add(new FileInfo(tempFile));
                }
            }

            return files;
        }

        /// <summary>
        /// Calculates the sum of the lengths of all the temp files, in bytes.
        /// </summary>
        /// <returns>The combined lengths of all the temp files, in bytes.</returns>
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
