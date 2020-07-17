using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using static AMDownloader.Common;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System;

namespace AMDownloader
{
    class AddDownloadViewModel : INotifyPropertyChanged
    {
        private readonly DownloaderViewModel _parentViewModel;
        private CancellationTokenSource _ctsClipboard;
        private CancellationToken _ctClipboard;
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
                    if (_ctsClipboard == null)
                    {
                        _ctsClipboard = new CancellationTokenSource();
                        _ctClipboard = _ctsClipboard.Token;

                        Task.Run(async () =>
                        {
                            while (!_ctClipboard.IsCancellationRequested)
                            {
                                await Task.Delay(1000);

                                string clip = _clipboardService.GetText();

                                if (clip.Contains("http") || clip.Contains("ftp"))
                                {
                                    if (!this.Urls.Contains(clip))
                                    {
                                        this.Urls += '\n' + clip;
                                        AnnouncePropertyChanged(nameof(this.Urls));
                                        _clipboardService.Clear();
                                    }
                                }
                            }

                            _ctsClipboard = null;
                            _ctClipboard = default;

                        }, _ctClipboard);
                    }
                }
                else
                {
                    if (_ctsClipboard != null)
                    {
                        _ctsClipboard.Cancel();
                    }
                }
            }
        }

        public RelayCommand AddCommand { get; private set; }
        public RelayCommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(DownloaderViewModel parentViewModel)
        {
            _parentViewModel = parentViewModel;
            AddCommand = new RelayCommand(Add);
            PreviewCommand = new RelayCommand(Preview);
            _clipboardService = new ClipboardObserver();
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

            if (Urls == null || SaveToFolder == null || !Directory.Exists(SaveToFolder))
            {
                return;
            }

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
            {
                SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;
            }

            foreach (var url in ListifyUrls())
            {
                AddItemToList(url);
            }

            if (AddToQueue && StartDownload)
            {
                Task.Run(async () => await _parentViewModel.MainQueueProcessor.StartAsync());
            }
        }

        private void AddItemToList(string url)
        {
            var fileName = GetValidFilename(SaveToFolder + Path.GetFileName(url), _parentViewModel.DownloadItemsList);
            DownloaderObjectModel dItem;

            if (AddToQueue)
            {
                dItem = new DownloaderObjectModel(ref _parentViewModel.Client, url, fileName, _parentViewModel.MainQueueProcessor);
                _parentViewModel.MainQueueProcessor.Add(dItem);
            }
            else
            {
                dItem = new DownloaderObjectModel(ref _parentViewModel.Client, url, fileName);
            }

            if (!AddToQueue && StartDownload)
            {
                Task.Run(async () => await dItem.StartAsync());
            }

            _parentViewModel.DownloadItemsList.Add(dItem);
            dItem.PropertyChanged += new PropertyChangedEventHandler(_parentViewModel.OnDownloadPropertyChange);
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
                        var newurl = url.Replace(bounds, i.ToString());
                        urlList.Add(newurl);
                    }
                }
                else
                {
                    // normal url
                    urlList.Add(url);
                }
            }

            return urlList;
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        class ClipboardObserver : IClipboard
        {
            public void SetText(string value)
            {
                Thread t = new Thread(() =>
                {
                    Clipboard.SetText(value);
                });

                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
            }
            public string GetText()
            {
                string val = string.Empty;

                Thread t = new Thread(() =>
                {
                    val = Clipboard.GetText();
                });

                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();

                return val;
            }
            public void Clear()
            {
                Thread t = new Thread(() =>
                {
                    Clipboard.Clear();
                });

                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
            }
        }
    }
}
