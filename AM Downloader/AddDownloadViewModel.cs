// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.Properties;
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
        private readonly BuildUrlsFromPatternsDelegate _buildUrlsFromPatterns;
        private readonly DisplayMessageDelegate _displayMessage;
        private bool _monitorClipboard;
        private CancellationTokenSource _ctsClipboard;
        private readonly ClipboardObserver _clipboardService;

        public event PropertyChangedEventHandler PropertyChanged;

        public ShowPreviewDelegate ShowPreview { get; private set; }
        public string Urls { get; set; }
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
                    _ctsClipboard?.Cancel();
                }
            }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(
            BuildUrlsFromPatternsDelegate buildUrlsFromPatterns,
            ShowPreviewDelegate showPreview,
            DisplayMessageDelegate displayMessage)
        {
            _buildUrlsFromPatterns = buildUrlsFromPatterns;
            _displayMessage = displayMessage;
            _clipboardService = new ClipboardObserver();

            AddCommand = new RelayCommand<object>(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand<object>(Preview, Add_CanExecute);

            if (Settings.Default.LastSavedLocation.Trim().Length > 0)
            {
                this.SaveToFolder = Settings.Default.LastSavedLocation;
            }
            else
            {
                this.SaveToFolder = AppPaths.DownloadsFolder;
            }
            this.Enqueue = Settings.Default.EnqueueAddedItems;
            this.StartDownload = Settings.Default.StartDownloadingAddedItems;
            this.Urls = string.Empty;
            this.ShowPreview = showPreview;

            var clipText = _clipboardService.GetText();
            if (clipText.Contains("http") || clipText.Contains("ftp")) this.Urls += clipText.Trim() + "\n";
        }

        private void Preview(object obj)
        {
            string[] urls = _buildUrlsFromPatterns(Urls.Split('\n').ToArray()).ToArray();
            string output = string.Empty;

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

            Settings.Default.EnqueueAddedItems = this.Enqueue;
            Settings.Default.StartDownloadingAddedItems = this.StartDownload;
            Settings.Default.Save();
        }

        private async Task MonitorClipboardAsync()
        {
            while (!_ctsClipboard.IsCancellationRequested)
            {
                var delay = Task.Delay(1000);
                List<string> source = Regex.Replace(_clipboardService.GetText(), @"\t|\r", "").Split('\n').ToList();
                List<string> dest = Regex.Replace(this.Urls, @"\r|\t", "").ToLower().Split('\n').ToList();
                foreach (var url in source)
                {
                    var f_url = Regex.Replace(url, @"\s", "");
                    if ((f_url.ToLower().StartsWith("http") || f_url.ToLower().StartsWith("ftp") || f_url.ToLower().StartsWith("www.")) && !dest.Contains(f_url.ToLower()))
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