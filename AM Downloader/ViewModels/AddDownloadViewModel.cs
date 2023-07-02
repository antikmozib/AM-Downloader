﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using AMDownloader.Properties;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AMDownloader.ViewModels
{
    internal class AddDownloadViewModel : INotifyPropertyChanged
    {
        private CancellationTokenSource _monitorClipboardCts;
        private TaskCompletionSource _monitorClipboardTcs;
        private readonly ShowWindowDelegate _showList;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool MonitorClipboard
        {
            get
            {
                return _monitorClipboardTcs != null && _monitorClipboardTcs.Task.Status != TaskStatus.RanToCompletion;
            }

            set
            {
                if (value == true)
                {
                    if (_monitorClipboardTcs == null || _monitorClipboardTcs.Task.Status == TaskStatus.RanToCompletion)
                    {
                        _monitorClipboardTcs = new TaskCompletionSource();
                        _monitorClipboardCts = new CancellationTokenSource();

                        var ct = _monitorClipboardCts.Token;

                        Task.Run(async () =>
                        {
                            await MonitorClipboardAsync(ct);

                            _monitorClipboardCts.Dispose();
                            RaisePropertyChanged(nameof(MonitorClipboard));
                            RaisePropertyChanged(nameof(IsClipboardMonitorSwitchingState));
                        });
                    }
                }
                else
                {
                    if (_monitorClipboardTcs != null && _monitorClipboardTcs.Task.Status != TaskStatus.RanToCompletion)
                    {
                        _monitorClipboardCts.Cancel();

                        RaisePropertyChanged(nameof(IsClipboardMonitorSwitchingState));
                    }
                }

                Settings.Default.MonitorClipboard = value;
            }
        }
        public bool IsClipboardMonitorSwitchingState
        {
            get
            {
                try
                {
                    if (_monitorClipboardTcs == null || _monitorClipboardCts == null)
                    {
                        return false;
                    }

                    return _monitorClipboardCts.IsCancellationRequested
                        && _monitorClipboardTcs.Task.Status != TaskStatus.RanToCompletion;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }
        public string Urls { get; set; }
        /// <summary>
        /// Returns the list of full URLs generated from the supplied patterned URLs.
        /// </summary>
        public List<string> GeneratedUrls => BuildUrlsFromPatterns(Urls.Split(Environment.NewLine).ToArray());
        public string SaveToFolder { get; set; }
        public bool Enqueue { get; set; }
        public bool StartDownload { get; set; }

        public ICommand AddCommand { get; private set; }
        public ICommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(ShowWindowDelegate showList)
        {
            _showList = showList;

            AddCommand = new RelayCommand<object>(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand<object>(Preview, Preview_CanExecute);

            MonitorClipboard = Settings.Default.MonitorClipboard;
            Urls = string.Empty;
            if (Settings.Default.LastDownloadLocation.Trim().Length > 0)
            {
                SaveToFolder = Settings.Default.LastDownloadLocation;
            }
            else
            {
                SaveToFolder = Common.Paths.UserDownloadsFolder;
            }
            Enqueue = Settings.Default.EnqueueAddedItems;
            StartDownload = Settings.Default.StartDownloadingAddedItems;

            if (!MonitorClipboard)
            {
                var clipText = GenerateValidUrl(ClipboardObserver.GetText());

                if (!string.IsNullOrEmpty(clipText))
                {
                    Urls += clipText + Environment.NewLine;
                }
            }
        }

        public void KillClipboardObserver()
        {
            try
            {
                _monitorClipboardCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {

            }
        }

        private void Preview(object obj)
        {
            _showList.Invoke(new ListViewerViewModel(GeneratedUrls.ToList(), "Generated URLs:", "Preview"));
        }

        private bool Preview_CanExecute(object obj)
        {
            return !string.IsNullOrWhiteSpace(Urls);
        }

        private void Add(object item)
        {
            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                SaveToFolder += Path.DirectorySeparatorChar;

            Settings.Default.EnqueueAddedItems = Enqueue;
            Settings.Default.StartDownloadingAddedItems = StartDownload;
        }

        private bool Add_CanExecute(object obj)
        {
            return !string.IsNullOrWhiteSpace(Urls) && !string.IsNullOrWhiteSpace(SaveToFolder);
        }

        /// <summary>
        /// Builds a list of full URLs from a list of patterned URLs.
        /// </summary>
        /// <param name="urls">A list of patterned URLs.</param>
        /// <returns>The list of URLs built from the supplied patterned URLs.</returns>
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
                    int minLength = bounds.Substring(1, bounds.IndexOf(':') - 1).Length;
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
        /// Extracts any valid URLs from the supplied <paramref name="value"/>,
        /// and then trims and returns them as a string separated by newlines.
        /// </summary>
        /// <param name="value">A list of strings (separated by newlines) in which
        /// to scan for valid URLs.</param>
        /// <returns>A list of validated and trimmed URLs (separated by newlines).</returns>
        private static string GenerateValidUrl(string value)
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

        private async Task MonitorClipboardAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && this is not null)
            {
                var newUrls = string.Empty;
                var delay = Task.Delay(1000, ct);
                var stopwatch = new Stopwatch();

                stopwatch.Start();

                if (string.IsNullOrWhiteSpace(Urls))
                {
                    newUrls = string.Join(Environment.NewLine, GenerateValidUrl(ClipboardObserver.GetText()));
                }
                else
                {
                    var existingUrls = Urls.Split(Environment.NewLine);
                    var incomingUrls = GenerateValidUrl(ClipboardObserver.GetText())
                        .Split(Environment.NewLine);

                    foreach (var url in incomingUrls)
                    {
                        if (existingUrls.Contains(url))
                        {
                            continue;
                        }

                        newUrls += Environment.NewLine + url;
                    }
                }

                Urls = Urls.TrimEnd() + newUrls;
                RaisePropertyChanged(nameof(Urls));

                stopwatch.Stop();
                Log.Debug(stopwatch.ElapsedMilliseconds.ToString());

                // must await within try/catch otherwise an OperationCanceledException
                // will be thrown and the Task will exit abruptly without setting
                // the result of _monitorClipboardTcs
                try
                {
                    await delay;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _monitorClipboardTcs.SetResult();
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}