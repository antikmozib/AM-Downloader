// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.Helpers;
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
        private bool _monitorClipboard;
        private CancellationTokenSource _ctsClipboard;
        private readonly ClipboardObserver _clipboardService;
        private readonly ShowWindowDelegate _showList;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Urls { get; set; }
        /// <summary>
        /// Returns the list of full URLs generated from the URL patterns.
        /// </summary>
        public List<string> GeneratedUrls => BuildUrlsFromPatterns(Urls.Split('\n').ToArray());
        public string SaveToFolder { get; set; }
        public bool StartDownload { get; set; }
        public bool Enqueue { get; set; }
        public bool MonitorClipboard
        {
            get { return _monitorClipboard; }
            set
            {
                _monitorClipboard = value;

                if (value == true)
                {
                    _ctsClipboard = new CancellationTokenSource();
                    Task.Run(async () => await MonitorClipboardAsync());
                }
                else
                {
                    try
                    {
                        _ctsClipboard?.Cancel();
                        _ctsClipboard.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {

                    }
                }
            }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(ShowWindowDelegate showList)
        {
            _clipboardService = new ClipboardObserver();

            AddCommand = new RelayCommand<object>(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand<object>(Preview, Add_CanExecute);

            if (Settings.Default.LastDownloadLocation.Trim().Length > 0)
            {
                this.SaveToFolder = Settings.Default.LastDownloadLocation;
            }
            else
            {
                this.SaveToFolder = Paths.UserDownloadsFolder;
            }
            this.Enqueue = Settings.Default.EnqueueAddedItems;
            this.StartDownload = Settings.Default.StartDownloadingAddedItems;
            this.Urls = string.Empty;
            this._showList = showList;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http") || clipText.Contains("ftp")) this.Urls += clipText.Trim() + "\n";
        }

        private void Preview(object obj)
        {
            this._showList.Invoke(new ListViewerViewModel(GeneratedUrls.ToList(), "Generated URLs:", "Preview"));
        }

        private bool Add_CanExecute(object obj)
        {
            if (this.Urls.Trim().Length == 0)
            {
                return false;
            }

            return true;
        }

        private void Add(object item)
        {
            if (Urls == null || SaveToFolder == null) return;

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder += Path.DirectorySeparatorChar;

            Settings.Default.EnqueueAddedItems = this.Enqueue;
            Settings.Default.StartDownloadingAddedItems = this.StartDownload;
        }

        private static List<string> BuildUrlsFromPatterns(params string[] urls)
        {
            var filteredUrls = urls.Select(o => o.Trim()).Where(o => o.Length > 0); // trim and discard empty
            var fullUrls = new List<string>();
            var pattern = @"(\[\d+:\d+\])";
            var regex = new Regex(pattern);

            foreach (var url in filteredUrls)
            {
                if (regex.Match(url).Success)
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
                        fullUrls.Add(regex.Replace(url, replacedData));
                    }
                }
                else
                {
                    // normal url
                    fullUrls.Add(url);
                }
            }

            return fullUrls;
        }

        private async Task MonitorClipboardAsync()
        {
            while (!_ctsClipboard.IsCancellationRequested)
            {
                var delay = Task.Delay(1000);
                List<string> source = Regex
                    .Replace(_clipboardService.GetText(), @"\t|\r", "")
                    .Split('\n')
                    .Where(o => o.Trim().Length > 0).ToList();
                List<string> dest = Regex
                    .Replace(this.Urls, @"\r|\t", "")
                    .ToLower()
                    .Split('\n').ToList();

                foreach (var url in source)
                {
                    var f_url = Regex.Replace(url, @"\s", "");

                    if ((f_url.ToLower().StartsWith("http") || f_url.ToLower().StartsWith("ftp") || f_url.ToLower().StartsWith("www."))
                        && !dest.Contains(f_url.ToLower()))
                    {
                        this.Urls += f_url + '\n';
                    }
                }

                RaisePropertyChanged(nameof(this.Urls));

                await delay;
            }
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}