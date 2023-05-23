// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

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
        private readonly AddItemsAsyncDelegate _addItemsAsync;
        private readonly DisplayMessageDelegate _displayMessage;
        private bool _monitorClipboard;
        private CancellationTokenSource _ctsClipboard;
        private readonly ClipboardObserver _clipboardService;

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
                {
                    Task.Run(async () => await MonitorClipboardAsync());
                }
                else
                {
                    if (_ctsClipboard != null) _ctsClipboard.Cancel();
                }
            }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(AddItemsAsyncDelegate addItemsAsync, ShowPreviewDelegate showPreview, DisplayMessageDelegate displayMessage)
        {
            _addItemsAsync = addItemsAsync;
            _displayMessage = displayMessage;
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
            this.Urls = string.Empty;
            this.ShowPreview = showPreview;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http") || clipText.Contains("ftp")) this.Urls += clipText.Trim() + "\n";
        }

        private void Preview(object obj)
        {
            string[] urls = ProcessUrlPatterns().ToArray();
            string output = String.Empty;

            if (urls.Length == 0) return;
            foreach (var url in urls)
            {
                output += url + "\n\n";
            }
            this.ShowPreview.Invoke(output);
        }

        private bool Add_CanExecute(object obj)
        {
            if (this.Urls.Trim().Length == 0) return false;
            return true;
        }

        private void Add(object item)
        {
            if (Urls == null || SaveToFolder == null) return;

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder += Path.DirectorySeparatorChar;

            Settings.Default.AddItemsToQueue = this.AddToQueue;
            Settings.Default.StartDownloadingAddedItems = this.StartDownload;
            Settings.Default.Save();

            Task.Run(async () => await _addItemsAsync.Invoke(SaveToFolder, AddToQueue, StartDownload, ProcessUrlPatterns().ToArray()));
        }

        private List<string> ProcessUrlPatterns()
        {
            string pattern = @"(\[\d*:\d*\])";
            var regex = new Regex(pattern);
            List<string> urlList = new List<string>();

            foreach (var url in Urls.Split('\n').ToList<string>())
            {
                if (url.Trim().Length == 0)
                {
                    // empty line
                    continue;
                }
                else if (regex.Match(url).Success)
                {
                    // url has patterns
                    string bounds = regex.Match(url).Value;

                    // patterns can be [1:20] or [01:20] - account for this difference
                    int minLength = bounds.Substring(1, bounds.IndexOf(":") - 1).Length;
                    int.TryParse(bounds.Substring(1, bounds.IndexOf(':') - 1), out int lBound);
                    int.TryParse(bounds.Substring(bounds.IndexOf(':') + 1, bounds.Length - bounds.IndexOf(':') - 2), out int uBound);

                    for (int i = lBound; i <= uBound; i++)
                    {
                        var replacedData = "";
                        if (i.ToString().Length < minLength)
                        {
                            for (int j = 0; j < (minLength - i.ToString().Length); j++)
                            {
                                replacedData += "0";
                            }
                        }
                        replacedData += i.ToString();
                        urlList.Add(regex.Replace(url, replacedData).Trim());
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