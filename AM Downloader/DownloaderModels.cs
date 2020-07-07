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
        public const long ONE_KB = 1024;
        public const long ONE_MB = ONE_KB * ONE_KB;
        public const long ONE_GB = ONE_KB * ONE_KB * ONE_KB;

        public class DownloaderItemModel : INotifyPropertyChanged
        {
            private HttpClient httpClient;
            private CancellationTokenSource ctsPause, ctsCancel;
            private CancellationToken ctPause, ctCancel;
            private TaskCompletionSource<DownloadStatus> tcsDownloadItem;
            private IProgress<int> reporterProgress;

            public event PropertyChangedEventHandler PropertyChanged;
            public enum DownloadStatus
            {
                Ready, Queued, Downloading, Paused, Pausing, Completed, Error, Cancelling
            }

            #region Properties
            public string Filename { get; private set; }
            public string Url { get; private set; }
            public string Destination { get; private set; }
            public DownloadStatus Status { get; private set; }
            public int Progress { get; private set; }
            public long TotalBytesCompleted { get; private set; }
            public long TotalBytesToDownload { get; private set; }
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
                    return PrettyNum<long>(this.TotalBytesToDownload);
                }
            }
            public string PrettyDownloadedSoFar
            {
                get
                {
                    return PrettyNum<long>(this.TotalBytesCompleted);
                }
            }
            #endregion

            public static string PrettyNum<T>(T num)
            {
                double result = 0;
                double.TryParse(num.ToString(), out result);

                if (result > ONE_GB)
                {
                    result = Math.Round(result / ONE_GB, 3);
                    return result.ToString() + " GB";
                }
                else if (result > ONE_MB)
                {
                    result = Math.Round(result / ONE_MB, 2);
                    return result.ToString() + " MB";
                }
                else if (result > ONE_KB)
                {
                    result = Math.Round(result / ONE_KB, 0);
                    return result.ToString() + " KB";
                }
                else
                {
                    return result.ToString() + " B";
                }
            }

            public bool TrySetDestination(string destination)
            {
                if (File.Exists(destination))
                {
                    throw new IOException("File already exists.");
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
                    throw new Exception();
                }

                this.Filename = Path.GetFileName(destination);
                this.Url = url;
                this.Destination = destination;
                this.Status = DownloadStatus.Ready;
                this.Progress = 0;
                this.TotalBytesCompleted = 0;
                this.TotalBytesToDownload = 0;
                this.BytesDownloadedThisSession = 0;
                this.KiloBytesPerSec = null;
                this.SupportsResume = false;

                Task.Run(async () =>
                {
                    // Determine total size to download ASYNC!
                    using (HttpResponseMessage response = await this.httpClient.GetAsync(this.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? 0;
                        AnnouncePropertyChanged("TotalBytesToDownload");
                        AnnouncePropertyChanged(nameof(this.PrettyTotalSize));
                    }
                });

                if (start)
                    Task.Run(async () => await this.StartAsync());
            }

            private async Task DownloadAsync(IProgress<int> progressReporter, long bytesDownloadedPreviously = 0)
            {
                CancellationToken linkedTokenSource;
                DownloadStatus _status = DownloadStatus.Error;
                HttpRequestMessage request;
                // _httpClient.DefaultRequestHeaders.Clear();

                ctsPause = new CancellationTokenSource();
                ctsCancel = new CancellationTokenSource();
                ctPause = ctsPause.Token;
                ctCancel = ctsCancel.Token;
                linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ctPause, ctCancel).Token;

                // resume download by requesting data from an advanced point
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get,
                    Headers = { Range = new RangeHeaderValue(bytesDownloadedPreviously, this.TotalBytesToDownload) }
                };

                using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
                using (FileStream destinationStream = new FileStream(this.Destination, FileMode.Append))
                using (BinaryWriter binaryWriter = new BinaryWriter(destinationStream))
                {
                    request.Dispose();
                    byte[] buffer = new byte[1024];
                    bool moreToRead = true;
                    long nextProgressUpdate = 1;
                    long progressUpdateFrequency;

                    this.KiloBytesPerSec = new long();
                    progressUpdateFrequency = this.TotalBytesToDownload / 100;
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
                            _status = DownloadStatus.Completed;
                            progressReporter.Report(100);
                        }
                        else
                        {
                            byte[] data = new byte[read];
                            buffer.ToList().CopyTo(0, data, 0, read);

                            binaryWriter.Write(data, 0, data.Length);

                            this.BytesDownloadedThisSession += read;
                            this.TotalBytesCompleted = bytesDownloadedPreviously + this.BytesDownloadedThisSession;

                            if ((this.TotalBytesCompleted >= nextProgressUpdate || this.TotalBytesCompleted == this.TotalBytesToDownload) &&
                                this.TotalBytesToDownload > 0)
                            {
                                progressReporter.Report((int)((double)this.TotalBytesCompleted / (double)this.TotalBytesToDownload * 100));

                                if (nextProgressUpdate <= this.TotalBytesToDownload - progressUpdateFrequency)
                                {
                                    nextProgressUpdate = this.TotalBytesCompleted + progressUpdateFrequency;
                                }

                                AnnouncePropertyChanged(nameof(this.BytesDownloadedThisSession));
                                AnnouncePropertyChanged(nameof(this.PrettyDownloadedSoFar));
                                AnnouncePropertyChanged("TotalBytesCompleted");
                            }
                        }
                    }
                }

                if (_status == DownloadStatus.Completed || _status == DownloadStatus.Ready)
                {
                    ctsCancel = null;
                }

                ctsPause = null;
                tcsDownloadItem.SetResult(_status);
            }

            private void StartMeasuringSpeed()
            {
                Stopwatch sw = new Stopwatch();

                sw.Start();

                Task.Run(async () =>
                {
                    while (this.Status == DownloadStatus.Downloading)
                    {

                        if (this.BytesDownloadedThisSession > 0 && TotalBytesCompleted <= this.TotalBytesToDownload && sw.ElapsedMilliseconds >= 1000)
                        {
                            this.KiloBytesPerSec = (this.BytesDownloadedThisSession / 1024) / (sw.ElapsedMilliseconds / 1000);
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

                if (ctsPause != null)
                {
                    // Download in progress...
                    return;
                }

                if (File.Exists(this.Destination))
                {
                    if (ctsPause == null && ctsCancel == null)
                    {

                        try
                        {
                            File.Delete(this.Destination);
                        }
                        catch (Exception ex)
                        {
                            throw ex;
                        }
                    }
                    else
                    {
                        bytesAlreadyDownloaded = new FileInfo(this.Destination).Length;
                    }
                }

                tcsDownloadItem = new TaskCompletionSource<DownloadStatus>(DownloadAsync(reporterProgress, bytesAlreadyDownloaded));

                this.BytesDownloadedThisSession = 0;
                this.Status = DownloadStatus.Downloading;
                AnnouncePropertyChanged("Status");

                await tcsDownloadItem.Task;

                switch (tcsDownloadItem.Task.Result)
                {
                    case (DownloadStatus.Ready):
                        this.Cancel();

                        break;

                    case (DownloadStatus.Paused):
                        this.Pause();

                        break;

                    case (DownloadStatus.Completed):
                        this.Status = DownloadStatus.Completed;
                        AnnouncePropertyChanged("Status");

                        break;
                }
            }

            public void Pause()
            {
                if (ctsPause == null && ctsCancel == null)
                {
                    return;
                }

                if (ctsPause != null)
                {
                    // Download in progress...
                    ctsPause.Cancel();
                }

                if (tcsDownloadItem != null && tcsDownloadItem.Task.Result != DownloadStatus.Ready)
                {
                    this.Status = DownloadStatus.Paused;
                    AnnouncePropertyChanged("Status");
                }
            }

            public void Cancel()
            {
                if (ctsPause != null)
                {
                    // Cancel ongoing task
                    ctsCancel.Cancel();
                }

                if (tcsDownloadItem != null && tcsDownloadItem.Task.Result != DownloadStatus.Completed)
                {
                    // Delete partially downloaded file

                    Task.Run(async () =>
                    {
                        int i = 0;

                        while (File.Exists(this.Destination) && i++ < 30)
                        {
                            Debug.Print("Try number: " + i + '\n');
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
                                continue;
                            }

                            await Task.Delay(1000);
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

        public class AddDownloaderItemModel
        {
            private ObservableCollection<DownloaderItemModel> _downloadList;
            private HttpClient _httpClient;

            public string Urls { get; set; }
            public string SaveToFolder { get; set; }
            public bool AddToQueue { get; set; }
            public bool Start { get; set; }
            public string Destination { get; private set; }

            public RelayCommand AddCommand { private get; set; }

            public AddDownloaderItemModel(ref HttpClient httpClient, ref ObservableCollection<DownloaderItemModel> downloadList)
            {
                _downloadList = downloadList;
                _httpClient = httpClient;
                AddCommand = new RelayCommand(Add);
            }

            public void Add(object item)
            {
                if (this.Urls == null)
                {
                    return;
                }

                if (this.SaveToFolder == null || this.SaveToFolder.Trim() == "" || !Directory.Exists(SaveToFolder))
                {
                    SaveToFolder = @"c:\users\antik\desktop\";
                }
                else
                {
                    if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                    {
                        SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;
                    }
                }

                foreach (var url in Urls.Split('\n').ToList<string>())
                {
                    _downloadList.Add(new DownloaderItemModel(ref _httpClient, url, GetValidFilename(SaveToFolder + Path.GetFileName(url))));
                }
            }
        }
    }
}
