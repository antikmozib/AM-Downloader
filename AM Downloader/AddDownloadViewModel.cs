// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AMDownloader
{
    internal class AddDownloadViewModel : INotifyPropertyChanged
    {
        private AddItemsAsyncDelegate _addItemsAsync;
        private bool _monitorClipboard;
        private CancellationTokenSource _ctsClipboard;
        private ClipboardObserver _clipboardService;

        public event PropertyChangedEventHandler PropertyChanged;

        public ShowPreviewDelegate ShowPreview { get; private set; }
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

        public AddDownloadViewModel(AddItemsAsyncDelegate addItemsAsync, ShowPreviewDelegate showPreview)
        {
            _addItemsAsync = addItemsAsync;
            AddCommand = new RelayCommand<object>(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand<object>(Preview, Add_CanExecute);

            _clipboardService = new ClipboardObserver();

            if (Settings.Default.LastSavedLocation.Trim().Length > 0)
            {
                this.SaveToFolder = Settings.Default.LastSavedLocation;
            }
            else
            {
                this.SaveToFolder = AppPaths.DownloadsFolder;
            }
            this.AddToQueue = Settings.Default.AddItemsToQueue;
            this.StartDownload = Settings.Default.StartDownloadingAddedItems;
            this.Urls = String.Empty;
            this.ShowPreview = showPreview;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http") || clipText.Contains("ftp")) this.Urls += clipText.Trim() + "\n";
        }

        internal void Preview(object obj)
        {
            string[] urls = ListifyUrls().ToArray();
            string output = String.Empty;

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

            this.ShowPreview.Invoke(output);
        }

        private bool Add_CanExecute(object obj)
        {
            if (this.Urls.Trim().Length == 0) return false;
            return true;
        }

        public void Add(object item)
        {
            if (Urls == null || SaveToFolder == null) return;

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;

            Settings.Default.AddItemsToQueue = this.AddToQueue;
            Settings.Default.StartDownloadingAddedItems = this.StartDownload;
            Settings.Default.Save();

            Task.Run(async () => await _addItemsAsync.Invoke(SaveToFolder, AddToQueue, StartDownload, ListifyUrls().ToArray()));
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
                        _clipboardService.Clear();
                    }
                }
                RaisePropertyChanged(nameof(this.Urls));
                await Task.Delay(1000);
            }

            _ctsClipboard = null;
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}