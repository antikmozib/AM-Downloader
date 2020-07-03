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

namespace AM_Downloader
{
    class DownloaderModels
    {
        static readonly string downloadFolderPath = @"c:\users\antik\downloads\";

        public class DownloaderItemModel : INotifyPropertyChanged
        {
            private HttpClient _httpClient;
            private CancellationTokenSource ctsPause, ctsCancel;
            private CancellationToken ctPause, ctCancel;
            private Task downloadItemTask;
            private TaskCompletionSource<DownloadStatus> downloadTaskCompletionSource;

            public event PropertyChangedEventHandler PropertyChanged;
            public enum DownloadStatus
            {
                Ready, Queued, Downloading, Paused, Completed, Error, Cancelling
            }

            #region Properties
            public string ShortFilename { get; private set; }
            public string Url { get; private set; }
            public string LongFilename { get; private set; }
            public DownloadStatus Status { get; private set; }
            public int Progress { get; private set; }
            public long BytesDownloadedSoFar { get; private set; }
            public long TotalBytesToDownload { get; private set; }
            public bool SupportsResume { get; private set; }
            #endregion

            public DownloaderItemModel(ref HttpClient httpClient, AddDownloaderItemModel addDownloadItem)
            {
                _httpClient = httpClient;

                // The file we're trying to download must NOT exist...
                if (File.Exists(addDownloadItem.Destination))
                {
                    // throw new Exception();
                }

                string fileName = Path.GetFileName(addDownloadItem.Url);
                string LongFilename = addDownloadItem.Destination;

                this.ShortFilename = Path.GetFileName(LongFilename);
                this.Url = addDownloadItem.Url;
                this.LongFilename = LongFilename;
                this.Status = DownloadStatus.Ready;
                this.Progress = 0;
                this.BytesDownloadedSoFar = 0;
                this.TotalBytesToDownload = 0;
                this.SupportsResume = false;

                // Determine total size to download
                Task.Run(async () =>
                {
                    using (HttpResponseMessage response = await _httpClient.GetAsync(this.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? 0;
                        RaisePropertyChanged("TotalBytesToDownload");
                    }
                });

                if (addDownloadItem.Start)
                    Start();
            }

            private async Task DownloadAsync(IProgress<int> progressReporter)
            {
                _httpClient.DefaultRequestHeaders.Clear();
                HttpRequestMessage request;

                if (this.Status == DownloadStatus.Paused)
                {
                    // resume download by requesting data from an advanced point
                    request = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this.Url),
                        Method = HttpMethod.Get,
                        Headers = { Range = new RangeHeaderValue(new FileInfo(this.LongFilename).Length, this.TotalBytesToDownload) }
                    };
                }
                else
                {
                    request = new HttpRequestMessage { RequestUri = new Uri(this.Url), Method = HttpMethod.Get };
                }

                this.Status = DownloadStatus.Downloading;
                RaisePropertyChanged("Status");

                HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
                {
                    using (FileStream destinationStream = new FileStream(this.LongFilename, FileMode.Append))
                    {
                        using (BinaryWriter binaryWriter = new BinaryWriter(destinationStream))
                        {
                            long? totalSize = response.Content.Headers.ContentLength;
                            long totalRead = 0;
                            byte[] buffer = new byte[4096];
                            bool moreToRead = true;

                            while (moreToRead == true)
                            {
                                if (ctCancel.IsCancellationRequested || ctPause.IsCancellationRequested)
                                {
                                    response.Dispose();
                                    request.Dispose();
                                    binaryWriter.Close();
                                    destinationStream.Close();

                                    if (ctPause.IsCancellationRequested)
                                    {
                                        downloadTaskCompletionSource.SetResult(DownloadStatus.Paused);
                                    }
                                    else
                                    {
                                        /*await Task.Delay(4000);*/
                                        downloadTaskCompletionSource.SetResult(DownloadStatus.Ready);
                                    }

                                    return;
                                }

                                int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);

                                if (read == 0)
                                {
                                    moreToRead = false;
                                }
                                else
                                {
                                    byte[] data = new byte[read];
                                    buffer.ToList().CopyTo(0, data, 0, read);

                                    binaryWriter.Write(data, 0, data.Length);

                                    totalRead += read;

                                    // prevent divison by zero error
                                    if (this.TotalBytesToDownload > this.BytesDownloadedSoFar && this.TotalBytesToDownload != 0)
                                    {
                                        double progressPercent = (double)this.BytesDownloadedSoFar / (double)this.TotalBytesToDownload * 100;
                                        progressReporter.Report((int)progressPercent);
                                    }

                                    this.BytesDownloadedSoFar = new FileInfo(this.LongFilename).Length;
                                    RaisePropertyChanged("BytesDownloadedSoFar");
                                }
                            }
                        }

                        // Force progress to 100 if download completed successfully
                        this.Progress = 100;
                        RaisePropertyChanged("Progress");

                        this.Status = DownloadStatus.Completed;
                        RaisePropertyChanged("Status");

                        response.Dispose();
                        request.Dispose();
                        downloadTaskCompletionSource.SetResult(DownloadStatus.Completed);
                    }
                }
            }

            public void Start(bool redownload = true)
            {
                // already downloading...
                if (this.Status == DownloadStatus.Downloading)
                    return;

                // if already downloaded, delete the file and restart
                if (File.Exists(this.LongFilename) && this.Status == DownloadStatus.Completed && redownload)
                {
                    try
                    {
                        File.Delete(this.LongFilename);
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }

                ctsCancel = new CancellationTokenSource();
                ctCancel = ctsCancel.Token;
                ctsPause = new CancellationTokenSource();
                ctPause = ctsPause.Token;

                Progress<int> progress = new Progress<int>((int value) =>
                {
                    this.Progress = value;
                    RaisePropertyChanged("Progress");
                });

                downloadTaskCompletionSource = new TaskCompletionSource<DownloadStatus>();

                downloadItemTask = new Task(async () => await DownloadAsync(progress));
                downloadItemTask.Start();
            }

            public async Task PauseAsync()
            {
                if (this.Status != DownloadStatus.Downloading)
                    return;

                ctsPause.Cancel();
                await downloadTaskCompletionSource.Task;

                this.Status = downloadTaskCompletionSource.Task.Result;
                RaisePropertyChanged("Status");
            }

            public async Task CancelAsync()
            {
                if ((this.Status != DownloadStatus.Downloading && 
                    this.Status != DownloadStatus.Paused) || 
                    this.Status == DownloadStatus.Cancelling)
                    return;

                this.Status = DownloadStatus.Cancelling;
                RaisePropertyChanged("Status");

                ctsCancel.Cancel();
                await downloadTaskCompletionSource.Task;

                try
                {
                    File.Delete(this.LongFilename);

                    this.Progress = 0;
                    RaisePropertyChanged("Progress");
                    this.BytesDownloadedSoFar = 0;
                    RaisePropertyChanged("BytesDownloadedSoFar");

                    this.Status = downloadTaskCompletionSource.Task.Result;
                }
                catch (Exception ex)
                {
                    this.Status = DownloadStatus.Error;
                }
                finally
                {
                    RaisePropertyChanged("Status");
                }
            }

            protected void RaisePropertyChanged(string prop)
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

            public string Url { get; set; }
            public string Destination { get; set; }
            public bool AddToQueue { get; set; }
            public bool Start { get; set; }

            public RelayCommand AddCommand { get; set; }

            public AddDownloaderItemModel(ref HttpClient httpClient, ref ObservableCollection<DownloaderItemModel> downloadList)
            {
                _downloadList = downloadList;
                _httpClient = httpClient;
                AddCommand = new RelayCommand(Add);
            }

            public void Add(object item)
            {
                if (this.Url == null)
                {
                    return;
                }

                if (this.Destination == null)
                {
                    Destination = GetValidFilename(@"c:\users\antik\desktop\" + Path.GetFileName(this.Url));
                }

                _downloadList.Add(new DownloaderItemModel(ref _httpClient, this));
            }
        }
    }
}
