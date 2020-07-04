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

namespace AM_Downloader
{
    class DownloaderModels
    {
        public interface IBasicFileInfo
        {
            public string Url { get; }
            public string Destination { get; }
        }

        public class DownloaderItemModel : INotifyPropertyChanged, IBasicFileInfo
        {
            private HttpClient httpClient;
            private CancellationTokenSource ctsPause, ctsCancel;
            private CancellationToken ctPause, ctCancel;
            private TaskCompletionSource<DownloadStatus> tcsDownloadItem;
            private IProgress<int> progressReporter;

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
            public long BytesDownloadedSoFar { get; private set; }
            public long TotalBytesToDownload { get; private set; }
            public bool SupportsResume { get; private set; }
            #endregion

            public DownloaderItemModel(ref HttpClient httpClient, AddDownloaderItemModel itemToAdd)
            {
                this.httpClient = httpClient;
                progressReporter = new Progress<int>((value) =>
                {
                    this.Progress = value;
                    RaisePropertyChanged("Progress");
                });

                if (File.Exists(itemToAdd.Destination))
                {
                    // The file we're trying to download must NOT exist...
                    throw new Exception();
                }

                this.Filename = Path.GetFileName(itemToAdd.Destination);
                this.Url = itemToAdd.Url;
                this.Destination = itemToAdd.Destination;
                this.Status = DownloadStatus.Ready;
                this.Progress = 0;
                this.BytesDownloadedSoFar = 0;
                this.TotalBytesToDownload = 0;
                this.SupportsResume = false;

                Task.Run(async () =>
                {
                    // Determine total size to download ASYNC!
                    using (HttpResponseMessage response = await this.httpClient.GetAsync(this.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        this.TotalBytesToDownload = response.Content.Headers.ContentLength ?? 0;
                        RaisePropertyChanged("TotalBytesToDownload");
                    }
                });

                if (itemToAdd.Start)
                    _ = StartAsync();
            }

            private async Task DownloadAsync(IProgress<int> progressReporter, CancellationToken ct, long resumptionPoint = 0)
            {
                HttpRequestMessage request;
                // _httpClient.DefaultRequestHeaders.Clear();

                // resume download by requesting data from an advanced point
                request = new HttpRequestMessage
                {
                    RequestUri = new Uri(this.Url),
                    Method = HttpMethod.Get,
                    Headers = { Range = new RangeHeaderValue(resumptionPoint, this.TotalBytesToDownload) }
                };

                using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
                using (FileStream destinationStream = new FileStream(this.Destination, FileMode.Append))
                using (BinaryWriter binaryWriter = new BinaryWriter(destinationStream))
                {
                    request.Dispose();

                    long? totalSize = response.Content.Headers.ContentLength;
                    long totalRead = 0;
                    byte[] buffer = new byte[1024];
                    bool moreToRead = true;

                    while (moreToRead == true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            if (ctPause.IsCancellationRequested)
                                tcsDownloadItem.SetResult(DownloadStatus.Paused);
                            if (ctCancel.IsCancellationRequested)
                                tcsDownloadItem.SetResult(DownloadStatus.Ready);
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

                            if (this.TotalBytesToDownload > this.BytesDownloadedSoFar && this.BytesDownloadedSoFar > 0)
                            {
                            // prevent divison by zero error
                                progressReporter.Report((int)((double)this.BytesDownloadedSoFar / (double)this.TotalBytesToDownload * 100));
                            }

                            this.BytesDownloadedSoFar = resumptionPoint + totalRead;
                            RaisePropertyChanged("BytesDownloadedSoFar");
                        }
                    }
                }

                tcsDownloadItem.SetResult(DownloadStatus.Completed);
            }

            public async Task StartAsync(bool downloadAgain = true)
            {
                long resumptionPoint = 0;
                
                if (ctsPause != null)
                {
                    // Download in progress...
                    return;
                }

                if (File.Exists(this.Destination))
                {
                    if (this.Status == DownloadStatus.Paused)
                    {
                        resumptionPoint = new FileInfo(this.Destination).Length;
                    }
                    else
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
                }

                ctsPause = new CancellationTokenSource();
                ctsCancel = new CancellationTokenSource();
                ctPause = ctsPause.Token;
                ctCancel = ctsCancel.Token;

                tcsDownloadItem = new TaskCompletionSource<DownloadStatus>(DownloadAsync(progressReporter, CancellationTokenSource.CreateLinkedTokenSource(ctPause, ctCancel).Token, resumptionPoint));

                try
                {
                    this.Status = DownloadStatus.Downloading;
                    RaisePropertyChanged("Status");

                    await tcsDownloadItem.Task;

                    if (tcsDownloadItem.Task.Result == DownloadStatus.Completed)
                    {
                        progressReporter.Report(100);
                        this.Status = DownloadStatus.Completed;
                        ctsCancel = null;
                        ctsPause = null;
                    }
                }
                catch
                {
                    this.Status = DownloadStatus.Error;
                }

                RaisePropertyChanged("Status");
            }

            public async Task PauseAsync()
            {
                if (ctsPause == null)
                {
                    // Nothing to pause
                    return;
                }

                ctsPause.Cancel();
                await tcsDownloadItem.Task;
                ctsPause = null;

                this.Status = DownloadStatus.Paused;
                RaisePropertyChanged("Status");
            }

            public async Task CancelAsync()
            {
                if (ctsCancel != null)
                {
                    // Task ongoing, request cancelation...
                    ctsCancel.Cancel();
                    await tcsDownloadItem.Task;
                }
                else if (ctsCancel == null && this.Status == DownloadStatus.Completed)
                {
                    // Cannot cancel a completed download
                    return;
                }

                ctsPause = null;
                ctsCancel = null;

                this.Status = DownloadStatus.Ready;
                RaisePropertyChanged("Status");

                if (File.Exists(this.Destination))
                {
                    try
                    {
                        File.Delete(this.Destination);
                        progressReporter.Report(0);
                        this.BytesDownloadedSoFar = 0;
                        RaisePropertyChanged("BytesDownloadedSoFar");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
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

        public class AddDownloaderItemModel : IBasicFileInfo
        {
            private ObservableCollection<DownloaderItemModel> _downloadList;
            private HttpClient _httpClient;

            public string Url { get; set; }
            public string Destination { get; set; }
            public bool AddToQueue { get; set; }
            public bool Start { get; set; }

            public RelayCommand AddCommand { private get; set; }

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
