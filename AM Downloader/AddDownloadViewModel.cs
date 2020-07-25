using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System;
using System.Windows.Input;
using static AMDownloader.Common;

namespace AMDownloader
{
    class AddDownloadViewModel : INotifyPropertyChanged
    {
        private readonly DownloaderViewModel _parentViewModel;
        private CancellationTokenSource _ctsClipboard;
        private bool _monitorClipboard;
        private ClipboardObserver _clipboardService;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Urls { get; set; }
        public string SaveToFolder { get; set; }
        public bool StartDownload { get; set; }
        public bool AddToQueue { get; set; }
        public bool MonitorClipboard
        {
            get
            {
                return _monitorClipboard;
            }
            set
            {
                _monitorClipboard = value;

                if (value == true)
                {
                    Task.Run(async () => await MonitorClipboardAsync(_ctsClipboard));
                }
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

            this.SaveToFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            this.AddToQueue = true;
            this.StartDownload = false;
            this.Urls = string.Empty;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http")) this.Urls += clipText;
        }

        public void Preview(object item)
        {
            foreach (var url in ListifyUrls())
            {
                Debug.WriteLine(url);
            }
        }

        public void Add(object item)
        {
            if (Urls == null || SaveToFolder == null || !Directory.Exists(SaveToFolder)) return;

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;

            foreach (var url in ListifyUrls())
                AddItemToList(url);

            if (AddToQueue && StartDownload)
                Task.Run(async () => await _parentViewModel.QueueProcessor.StartAsync());
        }

        private void AddItemToList(string url)
        {
            var fileName = GetValidFilename(SaveToFolder + Path.GetFileName(url));
            DownloaderObjectModel item;

            if (AddToQueue)
            {
                item = new DownloaderObjectModel(ref _parentViewModel.Client, url, fileName, _parentViewModel.QueueProcessor);
                _parentViewModel.QueueProcessor.Add(item);
            }
            else
            {
                item = new DownloaderObjectModel(ref _parentViewModel.Client, url, fileName);
            }

            if (!AddToQueue && StartDownload)
                Task.Run(async () => await item.StartAsync());

            _parentViewModel.DownloadItemsList.Add(item);
            item.PropertyChanged += new PropertyChangedEventHandler(_parentViewModel.OnDownloadPropertyChange);
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

        private async Task MonitorClipboardAsync(CancellationTokenSource cts)
        {
            cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000);

                string clip = _clipboardService.GetText();

                if ((clip.Contains("http") || clip.Contains("ftp")) && !this.Urls.Contains(clip))
                {
                    this.Urls += clip + '\n';
                    AnnouncePropertyChanged(nameof(this.Urls));
                }
            }

            cts = null;
            ct = default;
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}