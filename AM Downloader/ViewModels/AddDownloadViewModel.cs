// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.Models.Serializable;
using AMDownloader.Properties;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace AMDownloader.ViewModels
{
    public delegate (bool, string) ShowFolderBrowserDelegate();

    public class AddDownloadViewModel : INotifyPropertyChanged, ICloseable
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Closing;
        public event EventHandler Closed;

        public ShowWindowDelegate ShowList { get; set; }
        public ShowPromptDelegate ShowPrompt { get; set; }
        public ShowFolderBrowserDelegate ShowFolderBrowser { get; set; }
        public bool MonitorClipboard { get; set; }
        public string Urls { get; set; }
        /// <summary>
        /// Returns the list of URLs exploded from the supplied patterned URLs.
        /// </summary>
        public List<string> ExplodedUrls => ExplodeUrlsFromPatterns(Urls.Split(Environment.NewLine).ToArray());
        /// <summary>
        /// Contains the list of locations where files have been previously downloaded and saved to.
        /// </summary>
        public ObservableCollection<string> SavedLocations { get; }
        /// <summary>
        /// Gets or sets the location where the files will be downloaded and saved to.
        /// </summary>
        public string SaveLocation { get; set; }
        public bool Enqueue { get; set; }
        public bool StartDownload { get; set; }
        /// <summary>
        /// Returns <see langword="true"/> if <see cref="AddCommand"/> has been executed successfully.
        /// </summary>
        public bool ItemsAdded { get; private set; }

        public ICommand BrowseCommand { get; private set; }
        public ICommand AddCommand { get; private set; }
        public ICommand PreviewCommand { get; private set; }

        public AddDownloadViewModel()
        {
            MonitorClipboard = Settings.Default.MonitorClipboard;
            SavedLocations = new();
            Enqueue = Settings.Default.EnqueueAddedItems;
            StartDownload = Settings.Default.StartDownloadingAddedItems;
            ItemsAdded = false;

            BrowseCommand = new RelayCommand<object>(Browse, Browse_CanExecute);
            AddCommand = new RelayCommand<object>(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand<object>(Preview, Preview_CanExecute);

            // add the default download location
            SavedLocations.Add(Common.Paths.UserDownloadsFolder);
            SaveLocation = Common.Paths.UserDownloadsFolder;

            if (Settings.Default.RememberLastDownloadLocation)
            {
                // add the last used download location
                if (!string.IsNullOrWhiteSpace(Settings.Default.LastDownloadLocation)
                    && !IsSameLocation(Settings.Default.LastDownloadLocation, Common.Paths.UserDownloadsFolder))
                {
                    SavedLocations.Add(Settings.Default.LastDownloadLocation);
                    SaveLocation = Settings.Default.LastDownloadLocation;
                }

                // add all previously used download locations
                if (File.Exists(Common.Paths.SavedLocationsFile))
                {
                    try
                    {
                        var list = Common.Functions.Deserialize<SerializableSavedLocationList>(Common.Paths.SavedLocationsFile);
                        foreach (var item in list.Objects)
                        {
                            if (SavedLocationsContains(item.Path))
                            {
                                continue;
                            }
                            SavedLocations.Add(item.Path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Message, ex);
                    }
                }
            }
        }

        private void Browse(object obj)
        {
            var (selected, selectedPath) = ShowFolderBrowser.Invoke();

            if (selected)
            {
                if (!SavedLocationsContains(selectedPath))
                {
                    SavedLocations.Add(selectedPath);
                }
                SaveLocation = selectedPath;

                RaisePropertyChanged(nameof(SavedLocations));
                RaisePropertyChanged(nameof(SaveLocation));
            }
        }

        private bool Browse_CanExecute(object obj)
        {
            return true;
        }

        private void Preview(object obj)
        {
            ShowList.Invoke(new ListViewerViewModel(ExplodedUrls.ToList(), "URLs exploded from their patterns:", "Preview"));
        }

        private bool Preview_CanExecute(object obj)
        {
            return !string.IsNullOrWhiteSpace(Urls);
        }

        private void Add(object obj)
        {
            // ensure the selected location is valid and accessible

            bool isDirAccessible = false;

            if (Path.IsPathFullyQualified(SaveLocation))
            {
                try
                {
                    Directory.CreateDirectory(SaveLocation);

                    var f = File.Create(Path.Combine(SaveLocation, Path.GetRandomFileName()));
                    f.Close();
                    File.Delete(f.Name);

                    isDirAccessible = true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message, ex);
                }
            }

            if (!isDirAccessible)
            {
                ShowPrompt?.Invoke("The selected location is inaccessible.", "Add", PromptButton.OK, PromptIcon.Error);
                return;
            }

            ItemsAdded = true;
            Close();
        }

        private bool Add_CanExecute(object obj)
        {
            return !string.IsNullOrWhiteSpace(Urls) && !string.IsNullOrWhiteSpace(SaveLocation);
        }

        /// <summary>
        /// Explodes and builds a list of URLs from a list of patterned URLs.
        /// </summary>
        /// <param name="urls">A list of patterned URLs.</param>
        /// <returns>The list of URLs exploded from the supplied patterned URLs.</returns>
        private static List<string> ExplodeUrlsFromPatterns(params string[] urls)
        {
            var filteredUrls = urls.Select(o => o.Trim()).Where(o => o.Length > 0); // trim and discard empty
            var explodedUrls = new List<string>();
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
                        explodedUrls.Add(regex.Replace(url, replacedData));
                    }
                }
                else
                {
                    // normal url
                    explodedUrls.Add(url);
                }
            }

            return explodedUrls;
        }

        /// <summary>
        /// Extracts any valid URLs from the supplied <paramref name="value"/>,
        /// and then trims and returns them as a string separated by newlines.
        /// </summary>
        /// <param name="value">A list of strings (separated by newlines) in which
        /// to scan for valid URLs.</param>
        /// <returns>A list of validated and trimmed URLs (separated by newlines).</returns>
        public static string GenerateValidUrl(string value)
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

        public bool SavedLocationsContains(string value)
        {
            return SavedLocations.Where(o => IsSameLocation(o, value)).Any();
        }

        public static bool IsSameLocation(string locA, string locB)
        {
            locA = locA.Trim();
            locB = locB.Trim();

            return string.Compare(locA, locB, true) == 0;
        }

        public void Close()
        {
            RaiseEvent(Closing);

            // save settings
            Settings.Default.MonitorClipboard = MonitorClipboard;
            Settings.Default.EnqueueAddedItems = Enqueue;
            Settings.Default.StartDownloadingAddedItems = StartDownload;

            // save download locations

            Settings.Default.LastDownloadLocation = SaveLocation;

            var list = new SerializableSavedLocationList();
            foreach (var savedLocation in SavedLocations)
            {
                var item = new SerializableSavedLocation
                {
                    Path = savedLocation.ToString()
                };
                list.Objects.Add(item);
            }

            try
            {
                Common.Functions.Serialize(list, Common.Paths.SavedLocationsFile);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message, ex);
            }

            RaiseEvent(Closed);
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }
    }
}