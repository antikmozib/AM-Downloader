// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
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
using System.Windows;
using System.Windows.Input;

namespace AMDownloader.ViewModels
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
                SaveToFolder = Settings.Default.LastDownloadLocation;
            }
            else
            {
                SaveToFolder = Paths.UserDownloadsFolder;
            }
            Enqueue = Settings.Default.EnqueueAddedItems;
            StartDownload = Settings.Default.StartDownloadingAddedItems;
            Urls = string.Empty;
            _showList = showList;

            var clipText = GenerateValidUrl(_clipboardService.GetText());

            if (!string.IsNullOrEmpty(clipText))
            {
                Urls += clipText + '\n';
            }
        }

        private void Preview(object obj)
        {
            _showList.Invoke(new ListViewerViewModel(GeneratedUrls.ToList(), "Generated URLs:", "Preview"));
        }

        private bool Add_CanExecute(object obj)
        {
            if (Urls.Trim().Length == 0)
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

            Settings.Default.EnqueueAddedItems = Enqueue;
            Settings.Default.StartDownloadingAddedItems = StartDownload;
        }

        /// <summary>
        /// Builds and returns a list of URLs from a given list of URL patterns.
        /// </summary>
        /// <param name="urls">A list of URL patterns.</param>
        /// <returns>A list of URLs built from the provided URL patterns.</returns>
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
                            for (int j = 0; j < minLength - i.ToString().Length; j++)
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

        /// <summary>
        /// Extracts any valid URLs from the provided <paramref name="value"/>,
        /// and then trims and returns them as a string separated by newlines.
        /// </summary>
        /// <param name="value">A list of strings (separated by newlines) in which
        /// to scan for valid URLs.</param>
        /// <returns>A list of validated and trimmed URLs (separated by newlines).</returns>
        private string GenerateValidUrl(string value)
        {
            var urls = value.Split(Environment.NewLine);
            var output = new List<string>();

            for (int i = 0; i < urls.Length; i++)
            {
                var url = urls[i].Trim();

                if ((url.StartsWith("http") || url.StartsWith("ftp") || url.StartsWith("www.")) && !url.Contains(' '))
                {
                    output.Add(url);
                }
            }

            return string.Join(Environment.NewLine, output);
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
                    .Replace(Urls, @"\r|\t", "")
                    .ToLower()
                    .Split('\n').ToList();

                for (int i = 0; i < source.Count; i++)
                {
                    var url = GenerateValidUrl(source[i]);

                    if (!string.IsNullOrEmpty(url) && !dest.Contains(url.ToLower()))
                    {
                        Urls += url + '\n';
                    }
                }

                RaisePropertyChanged(nameof(Urls));

                await delay;
            }
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}