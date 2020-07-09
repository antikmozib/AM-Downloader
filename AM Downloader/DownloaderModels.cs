using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;
using System.Diagnostics;

namespace AM_Downloader
{
    class DownloaderModels
    {
        public const long KILOBYTE = 1024;
        public const long MEGABYTE = KILOBYTE * KILOBYTE;
        public const long GIGABYTE = MEGABYTE * KILOBYTE;
        public const long TERABYTE = GIGABYTE * KILOBYTE;

        public class DownloaderItemModel : INotifyPropertyChanged
        {
            private HttpClient httpClient;
            private CancellationTokenSource ctsPaused, ctsCanceled;
            private CancellationToken ctPause, ctCancel;
            private TaskCompletionSource<DownloadStatus> tcsDownloadItem;
            private IProgress<int> reporterProgress;

            public event PropertyChangedEventHandler PropertyChanged;
            public enum DownloadStatus
            {
                Ready, Queued, Downloading, Paused, Pausing, Complete, Error, Cancelling
            }

            public string Filename { get; private set; }
            public string Url { get; private set; }
            public string Destination { get; private set; }
            public DownloadStatus Status { get; private set; }
            public int Progress { get; private set; }
            public long TotalBytesCompleted { get; private set; }
            public long? TotalBytesToDownload { get; private set; }
            public long BytesDownloadedThisSession { get; private set; }
            public bool SupportsResume { get; private set; }
            public long? KiloBytesPerSec { get; private set; }
            public string PrettySpeed
            {
                get
                {
                    if (this.KiloBytesPerSec > 1024)
                    {
                        return ((double)this.KiloBytesPerSec / (double)1024).ToString("#0.00") + " MB/s";
                    }
                    else
                    {
                        if (this.KiloBytesPerSec == null)
                        {
                            return string.Empty;
                        }
                        else
                        {
                            return this.KiloBytesPerSec.ToString() + " KB/s";
                        }
                    }
                }
            }
            public string PrettyTotalSize
            {
                get
                {
                    return PrettyNum<long?>(this.TotalBytesToDownload);
                }
            }
            public string PrettyDownloadedSoFar
            {
                get
                {
                    return PrettyNum<long>(this.TotalBytesCompleted);
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
                    return true;
                }
            }

            public DownloaderItemModel(ref HttpClient httpClient, string url, string destination, bool start = false)
            {
                this.httpClient = httpClient;

                // capture sync context
                reporterProgress = new Progress<int>((value) =>
                {
                    this.Progress = value;
                    AnnouncePropertyChanged("Progress");
                });

                if (File.Exists(destination))
                {
                    // The file we're trying to download must NOT exist...
                    throw new IOException();
                }

                this.Filename = Path.GetFileName(destination);
                this.Url = url;
                this.Destination = destination;
                this.Status = DownloadStatus.Ready;
                this.Progress = 0;
                this.TotalBytesCompleted = 0;
                this.TotalBytesToDownload = null;
                this.BytesDownloadedThisSession = 0;
                this.KiloBytesPerSec = null;
                this.SupportsResume = false;

                Task.Run(async () =>
                {
                    // Determine total size to download ASYNC!
                    using (HttpResponseMessage response = await this.httpClient.GetAsync(this.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? null;
                        if (this.TotalBytesToDownload != null)
                        {
                            this.SupportsResume = true;
                        }
                        AnnouncePropertyChanged("TotalBytesToDownload");
                        AnnouncePropertyChanged(nameof(this.PrettyTotalSize));
                    }
                });

                if (start)
                    Task.Run(async () => await this.StartAsync());
            }

            private async Task<DownloadStatus> DownloadAsync(IProgress<int> progressReporter, long bytesDownloadedPreviously = 0)
            {
                CancellationToken linkedTokenSource;
                DownloadStatus _status = DownloadStatus.Error;
                HttpRequestMessage request;

                ctsPaused = new CancellationTokenSource();
                ctsCanceled = new CancellationTokenSource();

                if (this.SupportsResume)
                {
                    request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get,
                        Headers = { Range = new RangeHeaderValue(bytesDownloadedPreviously, this.TotalBytesToDownload) }
                    };
                }
                else
                {
                    request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get
                    };
                }

                ctPause = ctsPaused.Token;
                ctCancel = ctsCanceled.Token;
                linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ctPause, ctCancel).Token;

                using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
                using (FileStream destinationStream = new FileStream(this.Destination, FileMode.Append))
                using (BinaryWriter binaryWriter = new BinaryWriter(destinationStream))
                {
                    request.Dispose();
                    byte[] buffer = new byte[1024];
                    bool moreToRead = true;
                    long nextProgressUpdate;
                    long progressUpdateFrequency; // num bytes after which to report progress

                    this.KiloBytesPerSec = new long();

                    if (this.TotalBytesToDownload != null)
                    {
                        // report progress every 1%
                        progressUpdateFrequency = (long)this.TotalBytesToDownload / 100;
                    }
                    else
                    {
                        // report progress every 1 byte
                        progressUpdateFrequency = 1;
                    }

                    nextProgressUpdate = progressUpdateFrequency;

                    StartMeasuringSpeed();

                    while (moreToRead == true)
                    {
                        if (linkedTokenSource.IsCancellationRequested)
                        {
                            if (ctPause.IsCancellationRequested)
                            {
                                // Paused...
                                _status = DownloadStatus.Paused;
                            }
                            if (ctCancel.IsCancellationRequested)
                            {
                                // Cancelled...
                                _status = DownloadStatus.Ready;
                            }
                            break;
                        }

                        int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);

                        if (read == 0)
                        {
                            moreToRead = false;
                            _status = DownloadStatus.Complete;
                            progressReporter.Report(100);
                        }
                        else
                        {
                            byte[] data = new byte[read];
                            buffer.ToList().CopyTo(0, data, 0, read);

                            binaryWriter.Write(data, 0, data.Length);

                            this.BytesDownloadedThisSession += read;
                            this.TotalBytesCompleted = bytesDownloadedPreviously + this.BytesDownloadedThisSession;

                            if (this.TotalBytesCompleted >= nextProgressUpdate)
                            {
                                if (this.TotalBytesToDownload != null)
                                {
                                    progressReporter.Report((int)
                                        ((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100));

                                    if (nextProgressUpdate >= this.TotalBytesToDownload - progressUpdateFrequency)
                                    {
                                        progressUpdateFrequency = (long)this.TotalBytesToDownload - this.TotalBytesCompleted;
                                    }
                                }

                                nextProgressUpdate = this.TotalBytesCompleted + progressUpdateFrequency;
                                AnnouncePropertyChanged(nameof(this.BytesDownloadedThisSession));
                                AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                                AnnouncePropertyChanged(nameof(this.TotalBytesCompleted));
                            }
                        }
                    }
                }

                ctsCanceled = null;
                ctsPaused = null; // no more pausable
                tcsDownloadItem.SetResult(_status);
                return _status;
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
                            this.KiloBytesPerSec = (long)speed;
                            AnnouncePropertyChanged(nameof(this.KiloBytesPerSec));
                            AnnouncePropertyChanged(nameof(this.PrettySpeed));
                        }
                        await Task.Delay(1000);

                    }
                }).ContinueWith((cw) =>
                {
                    sw.Stop();
                    this.KiloBytesPerSec = null;
                    AnnouncePropertyChanged(nameof(this.KiloBytesPerSec));
                    AnnouncePropertyChanged(nameof(this.PrettySpeed));
                });
            }

            public async Task StartAsync(bool downloadAgain = true)
            {
                long bytesAlreadyDownloaded = 0;

                if (ctsPaused != null)
                {
                    // Download in progress
                    return;
                }
                else if (ctsCanceled != null || (ctsPaused == null && ctsCanceled == null))
                {
                    if (IsDownloadComplete())
                    {
                        return;
                    }
                    else
                    {
                        if (File.Exists(this.Destination))
                        {
                            // Download is paused; resume from specific point
                            bytesAlreadyDownloaded = new FileInfo(this.Destination).Length;
                        }
                    }
                }

                tcsDownloadItem = new TaskCompletionSource<DownloadStatus>(DownloadAsync(reporterProgress, bytesAlreadyDownloaded));

                this.BytesDownloadedThisSession = 0;
                this.Status = DownloadStatus.Downloading;
                AnnouncePropertyChanged("Status");

                await tcsDownloadItem.Task;
                switch (tcsDownloadItem.Task.Result)
                {
                    case (DownloadStatus.Ready): // a.k.a canceled
                        this.Cancel();
                        break;

                    case (DownloadStatus.Paused):
                        this.Pause();
                        break;

                    case (DownloadStatus.Complete):
                        this.Status = DownloadStatus.Complete;
                        AnnouncePropertyChanged("Status");
                        break;

                    default:
                        this.Cancel();
                        break;
                }
            }

            public void Pause()
            {
                if (IsDownloadComplete() || tcsDownloadItem == null)
                {
                    return;
                }

                if (ctsPaused != null)
                {
                    // Download in progress; request cancellation
                    ctsPaused.Cancel();

                    this.Status = DownloadStatus.Pausing;
                    AnnouncePropertyChanged(nameof(this.Status));
                }
                else if (tcsDownloadItem.Task.Result == DownloadStatus.Paused)
                {
                    // Download paused; update status
                    this.Status = DownloadStatus.Paused;
                    AnnouncePropertyChanged("Status");
                }
            }

            public void Cancel()
            {
                if (IsDownloadComplete() || tcsDownloadItem == null)
                {
                    return;
                }

                if (ctsCanceled != null)
                {
                    // Download in progress; request cancellation
                    ctsCanceled.Cancel();

                    this.Status = DownloadStatus.Cancelling;
                    AnnouncePropertyChanged(nameof(this.Status));
                }
                else if (tcsDownloadItem.Task.Result == DownloadStatus.Ready || tcsDownloadItem.Task.Result == DownloadStatus.Paused)
                {
                    // Delete partially downloaded file
                    Task.Run(async () =>
                    {
                        int numRetries = 0;

                        while (File.Exists(this.Destination) && numRetries++ < 30)
                        {
                            // if deletion fails, retry every 1 sec for 30 secs
                            try
                            {
                                File.Delete(this.Destination);
                                reporterProgress.Report(0);
                                this.TotalBytesCompleted = 0;
                                AnnouncePropertyChanged("TotalBytesCompleted");
                                AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                            }
                            catch (IOException)
                            {
                                await Task.Delay(1000);
                                continue; // file used by other apps; retry after 1 sec
                            }
                            catch (Exception)
                            {
                                break;
                            }
                        }
                    }).ContinueWith(t =>
                    {
                        this.Status = DownloadStatus.Ready;
                        AnnouncePropertyChanged("Status");
                    });
                }
            }

            protected void AnnouncePropertyChanged(string prop)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
            }

            private bool IsDownloadComplete()
            {
                if (this.TotalBytesToDownload > 0 && File.Exists(this.Destination) && new FileInfo(this.Destination).Length >= this.TotalBytesToDownload)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public class AddDownloaderItemModel
        {
            private ObservableCollection<DownloaderItemModel> _downloadList;
            private HttpClient _httpClient;

            public string Urls { get; set; }
            public string SaveToFolder { get; set; }
            public bool AddToQueue { get; set; }
            public bool Start { get; set; }

            public RelayCommand AddCommand { private get; set; }

            public AddDownloaderItemModel(ref HttpClient httpClient, ref ObservableCollection<DownloaderItemModel> downloadList)
            {
                this.SaveToFolder = null;
                this.Urls = null;
                this.Start = false;
                this.AddToQueue = true;

                _downloadList = downloadList;
                _httpClient = httpClient;
                AddCommand = new RelayCommand(Add);

            }

            public void Add(object item)
            {
                if (this.Urls == null || this.SaveToFolder == null || !Directory.Exists(SaveToFolder))
                {
                    return;
                }
                else
                {
                    if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                    {
                        SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;
                    }

                    foreach (var url in Urls.Split('\n').ToList<string>())
                    {
                        _downloadList.Add(new DownloaderItemModel(ref _httpClient, url, GetValidFilename(SaveToFolder + Path.GetFileName(url)), this.Start));
                    }
                }
            }
        }

        private static string PrettyNum<T>(T num)
        {
            double result = 0;
            double.TryParse(num.ToString(), out result);

            if (result > GIGABYTE)
            {
                result = Math.Round(result / GIGABYTE, 3);
                return result.ToString("#0.000") + " GB";
            }
            else if (result > MEGABYTE)
            {
                result = Math.Round(result / MEGABYTE, 2);
                return result.ToString("#0.00") + " MB";
            }
            else if (result > KILOBYTE)
            {
                result = Math.Round(result / KILOBYTE, 0);
                return result.ToString() + " KB";
            }
            else
            {
                return result.ToString() + " B";
            }
        }

        private static string GetValidFilename(string defaultFilename)
        {
            string _path = Path.GetDirectoryName(defaultFilename);
            string _filename = Path.GetFileName(defaultFilename);
            string result = _path + Path.DirectorySeparatorChar + _filename;

            int i = 0;
            while (File.Exists(result))
            {
                result = _path + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(_filename) + " (" + ++i + ")" + Path.GetExtension(_filename);
            };

            return result;
        }
    }
}
