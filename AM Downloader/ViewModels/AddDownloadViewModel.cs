// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.Models.Serialization;
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

        public Func<object, bool?> ShowList { get; set; }
        public ShowPromptDelegate ShowPrompt { get; set; }
        public ShowFolderBrowserDelegate ShowFolderBrowser { get; set; }
        public string Urls { get; set; }
        /// <summary>
        /// Returns the list of URLs exploded from the supplied patterned URLs.
        /// </summary>
        public List<string> ExplodedUrls => ExplodePatterns(Urls.Split(Environment.NewLine));
        /// <summary>
        /// Contains the list of locations where files have been previously downloaded and saved to.
        /// </summary>
        public ObservableCollection<string> SavedLocations { get; }
        /// <summary>
        /// Gets or sets the location where the files will be downloaded and saved to.
        /// </summary>
        public string SaveLocation { get; set; }
        public ObservableCollection<FileReplacementMode> ReplacementModes { get; }
        public FileReplacementMode ReplacementMode { get; set; }
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
            SavedLocations = new ObservableCollection<string>();
            ReplacementModes = new ObservableCollection<FileReplacementMode>();
            foreach (FileReplacementMode mode in Enum.GetValues(typeof(FileReplacementMode)))
            {
                ReplacementModes.Add(mode);
            }
            ReplacementMode = Settings.Default.ReplacementMode;
            Enqueue = Settings.Default.EnqueueAddedItems;
            StartDownload = Settings.Default.StartDownloadingAddedItems;
            ItemsAdded = false;

            BrowseCommand = new RelayCommand(Browse, Browse_CanExecute);
            AddCommand = new RelayCommand(Add, Add_CanExecute);
            PreviewCommand = new RelayCommand(Preview, Preview_CanExecute);

            // Add the default download location.
            SavedLocations.Add(Common.Paths.UserDownloadsFolder);
            SaveLocation = Common.Paths.UserDownloadsFolder;

            if (Settings.Default.RememberLastDownloadLocation)
            {
                // Add the last used download location.
                if (!string.IsNullOrWhiteSpace(Settings.Default.LastDownloadLocation)
                    && !IsSameLocation(Settings.Default.LastDownloadLocation, Common.Paths.UserDownloadsFolder))
                {
                    SavedLocations.Add(Settings.Default.LastDownloadLocation);
                    SaveLocation = Settings.Default.LastDownloadLocation;
                }

                // Add all previously used download locations.
                if (File.Exists(Common.Paths.SavedLocationsFile))
                {
                    try
                    {
                        var list = Common.Functions.Deserialize<SerializingSavedLocationList>(Common.Paths.SavedLocationsFile);
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
                        Log.Error(ex, ex.Message);
                    }
                }
            }
        }

        private void Browse()
        {
            var (selected, selectedPath) = ShowFolderBrowser();

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

        private bool Browse_CanExecute()
        {
            return true;
        }

        private void Preview()
        {
            ShowList(new ListViewerViewModel(ExplodedUrls, "URLs exploded from their patterns:", "Preview"));
        }

        private bool Preview_CanExecute()
        {
            return !string.IsNullOrWhiteSpace(Urls);
        }

        private void Add()
        {
            // Ensure the selected location is valid and accessible.

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
                    Log.Error(ex, ex.Message);
                }
            }

            if (!isDirAccessible)
            {
                ShowPrompt?.Invoke("The selected location is inaccessible.", "Add", icon: PromptIcon.Error);
                return;
            }

            ItemsAdded = true;
            Close();
        }

        private bool Add_CanExecute()
        {
            return !string.IsNullOrWhiteSpace(Urls) && !string.IsNullOrWhiteSpace(SaveLocation);
        }

        /// <summary>
        /// Explodes and builds a list of strings from a list of patterned strings.
        /// </summary>
        /// <param name="patterns">A list of patterned strings.</param>
        /// <returns>The list of strings exploded from the supplied patterned strings.</returns>
        private static List<string> ExplodePatterns(params string[] patterns)
        {
            var filteredStrings = patterns.Select(o => o.Trim()).Where(o => o.Length > 0); // Trim and discard empty.
            var explodedStrings = new List<string>();
            var pattern = @"(\[\d+:\d+\])";
            var regex = new Regex(pattern);

            foreach (var value in filteredStrings)
            {
                if (regex.Match(value).Success)
                {
                    // String has patterns.

                    string bounds = regex.Match(value).Value;

                    // Patterns can be [1:20] or [01:20] - account for this difference.
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
                        explodedStrings.Add(regex.Replace(value, replacedData));
                    }
                }
                else
                {
                    // Normal string.
                    explodedStrings.Add(value);
                }
            }

            return explodedStrings;
        }

        /// <summary>
        /// Extracts all valid URLs out of <paramref name="value"/>.
        /// </summary>
        /// <param name="value">A list of strings, separated by newlines, to check for the existence of valid URLs.</param>
        /// <returns>A list of validated URLs separated by newlines.</returns>
        public static string GenerateValidUrl(string value)
        {
            var urls = value.Split(Environment.NewLine);
            var output = new List<string>();

            for (int i = 0; i < urls.Length; i++)
            {
                if (Uri.TryCreate(urls[i], UriKind.Absolute, out var url))
                {
                    output.Add(url.AbsoluteUri);
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

            // Save settings.
            Settings.Default.ReplacementMode = ReplacementMode;
            Settings.Default.EnqueueAddedItems = Enqueue;
            Settings.Default.StartDownloadingAddedItems = StartDownload;

            // Save download locations.

            Settings.Default.LastDownloadLocation = SaveLocation;

            var list = new SerializingSavedLocationList();
            foreach (var savedLocation in SavedLocations)
            {
                var item = new SerializingSavedLocation
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
                Log.Error(ex, ex.Message);
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