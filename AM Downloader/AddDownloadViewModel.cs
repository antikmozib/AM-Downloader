using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;
using System.Threading;
using System.Windows.Input;
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

            AddCommand = new RelayCommand(Add);
            PreviewCommand = new RelayCommand(Preview);

            _clipboardService = new ClipboardObserver();

            this.SaveToFolder = ApplicationPaths.DownloadsFolder;
            this.AddToQueue = true;
            this.StartDownload = false;
            this.Urls = string.Empty;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http")) this.Urls += clipText;
        }

        public void Preview(object obj)
        {
            string[] urls = ListifyUrls().ToArray();
            string output = string.Empty;

            if (urls.Length==0) return;

            if (urls.Length > 7)
            {
                for (int i = 0; i < 3; i++)
                {
                    output += urls[i] + "\n\n";
                }
                for (int i = urls.Length - 3; i < urls.Length; i++)
                {
                    output += urls[i] + "\n\n";
                }
            }
            else {
                output = urls.ToString();
            }

            MessageBox.Show(output, "Preview", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void Add(object item)
        {
            if (Urls == null || SaveToFolder == null || !Directory.Exists(SaveToFolder)) return;

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;

            int counter = 0;

            foreach (var url in ListifyUrls())
                AddItemToList(url, counter++);

            if (AddToQueue && StartDownload)
                Task.Run(async () => await _parentViewModel.QueueProcessor.StartAsync(Settings.Default.MaxParallelDownloads));
        }

        private void AddItemToList(string url, int counter)
        {
            var fileName = GetValidFilename(SaveToFolder + Path.GetFileName(url));
            var checkIfUrlExists = from di in _parentViewModel.DownloadItemsList where di.Url == url select di;
            var checkIfDestinationExists = from di in _parentViewModel.DownloadItemsList where di.Destination == fileName select di;

            if (checkIfUrlExists.Count() > 0 || checkIfDestinationExists.Count() > 0) return;

            var item = new DownloaderObjectModel(ref _parentViewModel.Client, url, fileName, AddToQueue, _parentViewModel.OnDownloadPropertyChange, _parentViewModel.RefreshCollection);

            if (AddToQueue) _parentViewModel.QueueProcessor.Add(item);

            // Do not start more than 5 downloads at the same time
            if (!AddToQueue && StartDownload && counter < 5)
                Task.Run(async () => await item.StartAsync(Settings.Default.MaxConnectionsPerDownload));

            _parentViewModel.DownloadItemsList.Add(item);
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
                await Task.Delay(1000);

                string clip = _clipboardService.GetText();

                if ((clip.Contains("http") || clip.Contains("ftp")) && !this.Urls.Contains(clip))
                {
                    this.Urls += clip + '\n';
                    AnnouncePropertyChanged(nameof(this.Urls));
                }
            }

            _ctsClipboard = null;
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}