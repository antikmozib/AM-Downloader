using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using static AMDownloader.Common;

namespace AMDownloader
{
    public delegate void RefreshCollectionDelegate();

    class DownloaderObjectModel : INotifyPropertyChanged, IQueueable
    {
        #region Fields

        private CancellationTokenSource _ctsPaused, _ctsCanceled;
        private CancellationToken _ctPause, _ctCancel;
        private readonly IProgress<int> _progressReporter;
        private HttpClient _httpClient;
        private TaskCompletionSource<DownloadStatus> _taskCompletion;

        #endregion // Fields

        #region Properties

        public RefreshCollectionDelegate RefreshCollectionDel;
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

        #endregion // Properties

        #region Constructors

        public DownloaderObjectModel(ref HttpClient httpClient, string url, string destination) : this(ref httpClient, url, destination, false) { }

        public DownloaderObjectModel(ref HttpClient httpClient, string url, string destination, bool enqueue)
        {
            _httpClient = httpClient;

            // capture sync context
            _progressReporter = new Progress<int>((value) =>
            {
                this.Progress = value;
                AnnouncePropertyChanged(nameof(this.Progress));
            });

            /*if (File.Exists(destination))
            {
                // The file we're trying to download must NOT exist; halt object creation
                throw new IOException();
            }*/

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
            if (enqueue)
            {
                this.Enqueue();
            }

            Task.Run(async () => await DetermineTotalBytesToDownloadAsync()).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    // invalid url
                    this.TotalBytesToDownload = null;
                    this.Status = DownloadStatus.Error;
                    this.Dequeue();
                }
                AnnouncePropertyChanged(nameof(this.Status));
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
            IProgress<long> streamProgress = new Progress<long>((value) =>
            {
                this.BytesDownloadedThisSession += value;
                this.TotalBytesCompleted = bytesDownloadedPreviously + this.BytesDownloadedThisSession;
                AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));

                if (this.TotalBytesToDownload != null)
                {
                    double progress = (double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100;
                    _progressReporter.Report((int)progress);
                }
            });

            long pointFrequency = ((this.TotalBytesToDownload ?? 0) - bytesDownloadedPreviously) / numStreams;

            _ctsPaused = new CancellationTokenSource();
            _ctsCanceled = new CancellationTokenSource();
            _ctPause = _ctsPaused.Token;
            _ctCancel = _ctsCanceled.Token;
            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_ctPause, _ctCancel).Token;

            if (this.TotalBytesToDownload == null)
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

                    Debug.WriteLine("bytesDownloadedPreviously=" + bytesDownloadedPreviously + "  pointFrequency=" + pointFrequency + "   i=" + i + "   TotalBytesToDownload=" + this.TotalBytesToDownload);
                    Debug.WriteLine(fromPos + "-->" + toPos);

                    var request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get,
                        Headers = { Range = new RangeHeaderValue(fromPos, toPos) }
                    };

                    requests.Add(request);
                }
            }

            // Set up the tasks to process the requests
            foreach (var request in requests)
            {
                Task t = Task.Run(async () =>
                {
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
                }, linkedToken);

                tasks.Add(t);
            }

            // Run the tasks
            StartMeasuringSpeed();
            await Task.WhenAll(tasks);

            FileInfo[] files = new FileInfo[numStreams];
            for (int i = 0; i < numStreams; i++)
            {
                files[i] = new FileInfo(this.Destination + ".part" + i);
            }

            // Merge the streams
            this.Status = DownloadStatus.Merging;
            AnnouncePropertyChanged(nameof(this.Status));
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

        public async Task StartAsync(int numStreams = 5)
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

                    var fi = new FileInfo(this.Destination);
                    if (this.TotalBytesCompleted < fi.Length)
                    {
                        this.TotalBytesCompleted = fi.Length;
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

            if (RefreshCollectionDel != null) RefreshCollectionDel.Invoke();
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
        #endregion // Public methods
    }
}
