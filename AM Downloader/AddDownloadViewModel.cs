using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Threading;
using System.Windows.Input;
using System.Windows;
using AMDownloader.Properties;
using static AMDownloader.Common;

namespace AMDownloader
{
    class AddDownloadViewModel : INotifyPropertyChanged
    {
        private readonly DownloaderViewModel _parentViewModel;
        private bool _monitorClipboard;
        private CancellationTokenSource _ctsClipboard;
        private ClipboardObserver _clipboardService;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Urls { get; set; }
        public string SaveToFolder { get; set; }
        public bool StartDownload { get; set; }
        public bool AddToQueue { get; set; }
        public bool MonitorClipboard
        {
            get { return _monitorClipboard; }
            set
            {
                _monitorClipboard = value;

                if (value == true)
                    Task.Run(async () => await MonitorClipboardAsync());
                else
                {
                    if (_ctsClipboard != null) _ctsClipboard.Cancel();
                }
            }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(DownloaderViewModel parentViewModel)
        {
            _parentViewModel = parentViewModel;

            AddCommand = new RelayCommand(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand(Preview, Add_CanExecute);

            _clipboardService = new ClipboardObserver();

            if (Directory.Exists(Settings.Default.LastSavedLocation))
            {
                this.SaveToFolder = Settings.Default.LastSavedLocation;
            }
            else
            {
                this.SaveToFolder = ApplicationPaths.DownloadsFolder;
            }
            this.AddToQueue = true;
            this.StartDownload = false;
            this.Urls = string.Empty;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http") || clipText.Contains("ftp")) this.Urls += clipText.Trim() + "\n";
        }

        public void Preview(object obj)
        {
            string[] urls = ListifyUrls().ToArray();
            string output = string.Empty;

            if (urls.Length == 0) return;

            if (urls.Length > 7)
            {
                for (int i = 0; i < 3; i++)
                {
                    output += urls[i] + "\n\n";
                }
                output += "... " + (urls.Length - 6).ToString() + " more files ...\n\n";
                for (int i = urls.Length - 3; i < urls.Length; i++)
                {
                    output += urls[i] + "\n\n";
                }
            }
            else
            {
                foreach (var url in urls)
                {
                    output += url + "\n\n";
                }
            }

            MessageBox.Show(output, "Preview", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        bool Add_CanExecute(object obj)
        {
            if (this.Urls.Trim().Length == 0) return false;
            return true;
        }

        public void Add(object item)
        {
            if (Urls == null || SaveToFolder == null) return;

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;

            int counter = 0;

            foreach (var url in ListifyUrls())
                AddItemToList(url, counter++);

            if (AddToQueue && StartDownload)
                Task.Run(async () => await _parentViewModel.QueueProcessor.StartAsync(Settings.Default.MaxConnectionsPerDownload));
        }

        private void AddItemToList(string url, int counter)
        {
            var fileName = GetValidFilename(SaveToFolder + Path.GetFileName(url));
            var checkifUrlExists = from di in _parentViewModel.DownloadItemsList where di.Url == url select di;
            var checkIfDestinationExists = from di in _parentViewModel.DownloadItemsList where di.Destination == fileName select di;
            var sameItems = checkIfDestinationExists.Intersect(checkifUrlExists);

            if (sameItems.Count() > 0) return;

            var item = new DownloaderObjectModel(
                ref _parentViewModel.Client,
                url,
                fileName,
                AddToQueue,
                _parentViewModel.Download_Started,
                _parentViewModel.Download_Stopped,
                _parentViewModel.Download_Enqueued,
                _parentViewModel.Download_Dequeued,
                _parentViewModel.Download_Finished,
                _parentViewModel.Download_PropertyChanged,
                _parentViewModel.RefreshCollection);

            _parentViewModel.DownloadItemsList.Add(item);
            if (AddToQueue) _parentViewModel.QueueProcessor.Add(item);

            // Do not start more than MaxParallelDownloads at the same time
            if (!AddToQueue && StartDownload)
            {
                if (counter < Settings.Default.MaxParallelDownloads)
                {
                    Task.Run(async () => await item.StartAsync(Settings.Default.MaxConnectionsPerDownload));
                }
            }
        }

        private List<string> ListifyUrls()
        {
            string pattern = @"[[]\d+[:]\d+[]]";
            List<string> urlList = new List<string>();

            foreach (var url in Urls.Split('\n').ToList<string>())
            {
                if (url.Trim().Length == 0)
                {
                    // empty line
                    continue;
                }
                else if (Regex.Match(url, pattern).Success)
                {
                    // url has patterns
                    string bounds = Regex.Match(url, pattern).Value;
                    int lBound = 0, uBound = 0;

                    int.TryParse(bounds.Substring(1, bounds.IndexOf(':') - 1), out lBound);
                    int.TryParse(bounds.Substring(bounds.IndexOf(':') + 1, bounds.Length - bounds.IndexOf(':') - 2), out uBound);

                    for (int i = lBound; i <= uBound; i++)
                    {
                        var newurl = url.Replace(bounds, i.ToString()).Trim();
                        urlList.Add(newurl);
                    }
                }
                else
                {
                    // normal url
                    urlList.Add(url.Trim());
                }
            }

            return urlList;
        }

        private async Task MonitorClipboardAsync()
        {
            _ctsClipboard = new CancellationTokenSource();

            while (!_ctsClipboard.Token.IsCancellationRequested)
            {
                List<string> source = Regex.Replace(_clipboardService.GetText(), @"\t|\r", "").Split('\n').ToList();
                List<string> dest = Regex.Replace(this.Urls, @"\r|\t", "").Split('\n').ToList();
                foreach (var url in source)
                {
                    var f_url = Regex.Replace(url, @"\n", "");
                    if ((f_url.Contains("http") || f_url.Contains("ftp")) && !dest.Contains(f_url))
                    {
                        this.Urls += f_url + '\n';
                    }
                }
                AnnouncePropertyChanged(nameof(this.Urls));
                await Task.Delay(1000);
            }

            _ctsClipboard = null;
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private bool UrlExists(string url)
        {
            List<string> list = this.Urls.Split('\n').ToList<string>();
            return list.Contains(url);
        }
    }
}