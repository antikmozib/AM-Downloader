﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.Helpers;
using AMDownloader.ObjectModel.Serializable;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using AMDownloader.RequestThrottling;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Serialization;

namespace AMDownloader
{
    internal delegate Task AddItemsAsyncDelegate(string destination, bool enqueue, bool start, params string[] urls);

    internal delegate void ShowPreviewDelegate(string preview);

    internal delegate MessageBoxResult DisplayMessageDelegate(
        string message, string title = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.OK);

    internal delegate void ShowUrlsDelegate(List<string> urls, string caption, string infoLabel);

    internal enum Category
    {
        All, Ready, Queued, Downloading, Paused, Finished, Errored
    }

    internal class DownloaderViewModel : INotifyPropertyChanged, IClosing
    {
        #region Fields

        /// <summary>
        /// Gets the interval between refreshing the CollectionView.
        /// </summary>
        private const int _collectionRefreshDelay = 1000;
        private readonly DisplayMessageDelegate _displayMessage;
        private readonly ClipboardObserver _clipboardService;
        private readonly object _lockDownloadItemsList;
        private readonly object _lockBytesDownloaded;
        private readonly object _lockBytesTransferredOverLifetime;
        private readonly object _lockCtsRefreshViewList;
        private readonly SemaphoreSlim _semaphoreMeasuringSpeed;
        private readonly SemaphoreSlim _semaphoreUpdatingList;
        private readonly SemaphoreSlim _semaphoreRefreshingView;
        private CancellationTokenSource _ctsUpdatingList;
        private readonly List<CancellationTokenSource> _ctsRefreshViewList;
        private RequestThrottler _requestThrottler;
        private readonly HttpClient _client;
        private readonly ShowUrlsDelegate _showUrls;
        private bool _resetAllSettingsOnClose;
        private readonly IProgress<long> _progressReporter;

        #endregion

        #region Properties

        public ObservableCollection<DownloaderObjectModel> DownloadItemsList { get; private set; }
        public ObservableCollection<Category> CategoriesList { get; }
        public ICollectionView CollectionView { get; private set; }
        public QueueProcessor QueueProcessor { get; }
        public int Progress { get; private set; }
        public long BytesDownloadedThisSession { get; private set; }
        public long? Speed { get; private set; }
        public int Count { get; private set; }
        public int ReadyCount { get; private set; }
        public int DownloadingCount { get; private set; }
        public int QueuedCount { get; private set; }
        public int FinishedCount { get; private set; }
        public int ErroredCount { get; private set; }
        public int PausedCount { get; private set; }
        public bool IsBackgroundWorking
        {
            get
            {
                try
                {
                    return _ctsUpdatingList != null;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }
        public bool IsDownloading => DownloadingCount > 0;
        public string Status { get; private set; }

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Commands

        public ICommand AddCommand { get; private set; }
        public ICommand StartCommand { get; private set; }
        public ICommand RemoveCommand { private get; set; }
        public ICommand CancelCommand { private get; set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand OpenContainingFolderCommand { get; private set; }
        public ICommand StartQueueCommand { get; private set; }
        public ICommand StopQueueCommand { get; private set; }
        public ICommand CategoryChangedCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand EnqueueCommand { get; private set; }
        public ICommand DequeueCommand { get; private set; }
        public ICommand DeleteFileCommand { get; private set; }
        public ICommand RecheckCommand { get; private set; }
        public ICommand RedownloadCommand { get; private set; }
        public ICommand CopyLinkToClipboardCommand { get; private set; }
        public ICommand ClearFinishedDownloadsCommand { get; private set; }
        public ICommand CancelBackgroundTaskCommand { get; private set; }
        public ICommand CheckForUpdatesCommand { get; private set; }
        public ICommand UIClosedCommand { get; private set; }

        #endregion

        #region Constructors

        public DownloaderViewModel(DisplayMessageDelegate displayMessage, ShowUrlsDelegate showUrls)
        {
            DownloadItemsList = new();
            DownloadItemsList.CollectionChanged += DownloadItemsList_CollectionChanged;

            CategoriesList = new ObservableCollection<Category>();

            QueueProcessor = new QueueProcessor(
                Settings.Default.MaxParallelDownloads,
                QueueProcessor_PropertyChanged,
                QueueProcessor_Started,
                QueueProcessor_Stopped,
                QueueProcessor_ItemEnqueued,
                QueueProcessor_ItemDequeued);

            CollectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            CollectionView.CurrentChanged += CollectionView_CurrentChanged;

            this.Progress = 0;
            this.BytesDownloadedThisSession = 0;
            this.Speed = null;
            this.Count = 0;
            this.ReadyCount = 0;
            this.DownloadingCount = 0;
            this.QueuedCount = 0;
            this.FinishedCount = 0;
            this.ErroredCount = 0;
            this.PausedCount = 0;
            this.Status = "Ready";
            this._progressReporter = new Progress<long>(value =>
            {
                Monitor.Enter(_lockBytesDownloaded);
                try
                {
                    this.BytesDownloadedThisSession += value;
                }
                finally
                {
                    Monitor.Exit(_lockBytesDownloaded);
                }
            });

            _client = new HttpClient();
            _requestThrottler = new RequestThrottler(AppConstants.RequestThrottlerInterval);
            _clipboardService = new ClipboardObserver();
            _semaphoreMeasuringSpeed = new SemaphoreSlim(1);
            _semaphoreUpdatingList = new SemaphoreSlim(1);
            _semaphoreRefreshingView = new SemaphoreSlim(1);
            _ctsUpdatingList = null;
            _ctsRefreshViewList = new();
            _displayMessage = displayMessage;
            _lockDownloadItemsList = DownloadItemsList;
            _lockBytesDownloaded = this.BytesDownloadedThisSession;
            _lockBytesTransferredOverLifetime = Settings.Default.BytesTransferredOverLifetime;
            _lockCtsRefreshViewList = _ctsRefreshViewList;
            _showUrls = showUrls;
            _resetAllSettingsOnClose = false;

            AddCommand = new RelayCommand<object>(Add);
            StartCommand = new RelayCommand<object>(Start);
            RemoveCommand = new RelayCommand<object>(Remove);
            CancelCommand = new RelayCommand<object>(Cancel);
            PauseCommand = new RelayCommand<object>(Pause);
            OpenCommand = new RelayCommand<object>(Open);
            OpenContainingFolderCommand = new RelayCommand<object>(OpenContainingFolder);
            StartQueueCommand = new RelayCommand<object>(StartQueue);
            StopQueueCommand = new RelayCommand<object>(StopQueue);
            CategoryChangedCommand = new RelayCommand<object>(CategoryChanged);
            OptionsCommand = new RelayCommand<object>(ShowOptions);
            EnqueueCommand = new RelayCommand<object>(Enqueue);
            DequeueCommand = new RelayCommand<object>(Dequeue);
            DeleteFileCommand = new RelayCommand<object>(DeleteFile);
            RecheckCommand = new RelayCommand<object>(Recheck);
            RedownloadCommand = new RelayCommand<object>(Redownload);
            CopyLinkToClipboardCommand = new RelayCommand<object>(CopyLinkToClipboard);
            ClearFinishedDownloadsCommand = new RelayCommand<object>(ClearFinishedDownloads);
            CancelBackgroundTaskCommand = new RelayCommand<object>(
                CancelBackgroundTask, CancelBackgroundTask_CanExecute);
            CheckForUpdatesCommand = new RelayCommand<object>(CheckForUpdates);
            UIClosedCommand = new RelayCommand<object>(UIClosed);

            foreach (Category cat in (Category[])Enum.GetValues(typeof(Category)))
            {
                CategoriesList.Add(cat);
            }

            // Load last selected category
            if (string.IsNullOrEmpty(Settings.Default.LastSelectedCatagory))
            {
                SwitchCategory(Category.All);
            }
            else
            {
                SwitchCategory((Category)Enum.Parse(typeof(Category), Settings.Default.LastSelectedCatagory));
            }

            // Check for updates
            if (Settings.Default.AutoCheckForUpdates)
            {
                Task.Run(async () => await TriggerUpdateCheckAsync(true));
            }

            // Populate history
            _ctsUpdatingList = new CancellationTokenSource();
            var ct = _ctsUpdatingList.Token;

            Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();

                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                try
                {
                    if (Directory.Exists(AppPaths.LocalAppDataFolder))
                    {
                        this.Status = "Restoring data...";
                        RaisePropertyChanged(nameof(this.Status));
                    }
                    else
                    {
                        return;
                    }

                    SerializableDownloaderObjectModelList source;
                    var xmlReader = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));

                    using (var streamReader = new StreamReader(AppPaths.DownloadsHistoryFile))
                    {
                        source = (SerializableDownloaderObjectModelList)xmlReader.Deserialize(streamReader);
                    }

                    var sourceObjects = source.Objects.ToArray();
                    var itemsToAdd = new DownloaderObjectModel[sourceObjects.Length];
                    var itemsToEnqueue = new List<IQueueable>();
                    var total = sourceObjects.Length;

                    for (int i = 0; i < sourceObjects.Length; i++)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (sourceObjects[i] == null) continue;

                        int progress = (int)((double)(i + 1) / total * 100);
                        this.Progress = progress;
                        this.Status = "Restoring " + (i + 1) + " of " + total + ": " + sourceObjects[i].Url;
                        RaisePropertyChanged(nameof(this.Progress));
                        RaisePropertyChanged(nameof(this.Status));

                        var item = new DownloaderObjectModel(
                            _client,
                            sourceObjects[i].Url,
                            sourceObjects[i].Destination,
                            sourceObjects[i].DateCreated,
                            sourceObjects[i].TotalBytesToDownload,
                            sourceObjects[i].StatusCode,
                            sourceObjects[i].Status,
                            Download_Created,
                            Download_Started,
                            Download_Stopped,
                            Download_PropertyChanged,
                            _progressReporter);

                        if (sourceObjects[i].IsQueued)
                        {
                            itemsToEnqueue.Add(item);
                        }

                        itemsToAdd[i] = item;
                    }

                    this.Status = "Listing...";
                    RaisePropertyChanged(nameof(this.Status));

                    AddObjects(itemsToAdd);
                    QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                }
                catch
                {
                }
                finally
                {
                    _ctsUpdatingList = null;
                    this.Progress = 0;
                    this.Status = "Ready";
                    RaisePropertyChanged(nameof(this.Progress));
                    RaisePropertyChanged(nameof(this.Status));
                    RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                    _semaphoreUpdatingList.Release();

                    RefreshCollection();
                }
            });
        }

        #endregion

        #region Command methods

        private void CategoryChanged(object obj)
        {
            if (obj == null) return;
            SwitchCategory((Category)obj);
        }

        private void Start(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            Task.Run(async () => await StartDownloadAsync(false, items));
        }

        private void Pause(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                item.Pause();
            }
        }

        private void Cancel(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                if (QueueProcessor.IsQueued(item))
                {
                    QueueProcessor.Dequeue(item);
                }

                item.Cancel();
            }
        }

        private void Add(object obj)
        {
            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            var win = new AddDownloadWindow();
            var vm = new AddDownloadViewModel(win.Preview);

            win.DataContext = vm;
            win.Owner = obj as Window;

            if (win.ShowDialog() == true)
            {
                _ctsUpdatingList = new CancellationTokenSource();

                Task.Run(async () => await AddItemsAsync(
                    vm.SaveToFolder,
                    vm.Enqueue,
                    vm.StartDownload,
                    vm.GeneratedUrls.ToArray()));
            }
        }

        private void Remove(object obj)
        {
            if (obj == null) return;

            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            _ctsUpdatingList = new CancellationTokenSource();

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            Task.Run(async () => await RemoveObjectsAsync(false, items)).ContinueWith(t => RefreshCollection());
        }

        private void Open(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsFinished = from item in items
                                where item.Status == DownloadStatus.Finished
                                where new FileInfo(item.Destination).Exists
                                select item;
            if (itemsFinished.Count() > 1)
            {
                var r = _displayMessage.Invoke(
                    "You have selected to open " + itemsFinished.Count() + " files.\n\n" +
                    "Opening too many files at the same file may cause the system to crash.\n\nDo you wish to proceed?",
                    "Open", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No);

                if (r == MessageBoxResult.No) return;
            }
            foreach (var item in itemsFinished)
            {
                Process.Start("explorer.exe", "\"" + item.Destination + "\"");
            }
        }

        private void OpenContainingFolder(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsOpenable = from item in items
                                where File.Exists(item.Destination) ||
                                Directory.Exists(Path.GetDirectoryName(item.Destination))
                                select item;
            if (itemsOpenable.Count() > 1)
            {
                var result = _displayMessage.Invoke(
                    "You have selected to open " + items.Count + " folders.\n\n" +
                    "Opening too many folders at the same time may cause the system to crash.\n\n" +
                    "Do you wish to proceed?", "Open Folder",
                    MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }
            foreach (var item in itemsOpenable)
            {
                if (File.Exists(item.Destination))
                {
                    Process.Start("explorer.exe", "/select, \"\"" + item.Destination + "\"\"");
                }
                else if (Directory.Exists(Path.GetDirectoryName(item.Destination)))
                {
                    Process.Start("explorer.exe", Path.GetDirectoryName(item.Destination));
                }
            }
        }

        private void StartQueue(object obj)
        {
            Task.Run(async () =>
            {
                await QueueProcessor.StartAsync();
            });
        }

        private void StopQueue(object obj)
        {
            QueueProcessor.Stop();
        }

        private void ShowOptions(object obj)
        {
            var win = new OptionsWindow();
            var vm = new OptionsViewModel();

            win.DataContext = vm;
            win.Owner = obj as Window;

            win.ShowDialog();

            if (vm.ResetSettingsOnClose)
            {
                _resetAllSettingsOnClose = true;
            }
        }

        private void Enqueue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                QueueProcessor.Enqueue(item);
            }
        }

        private void Dequeue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            QueueProcessor.Dequeue(items);
        }

        private void DeleteFile(object obj)
        {
            if (obj == null) return;

            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            _ctsUpdatingList = new CancellationTokenSource();

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            Task.Run(async () => await RemoveObjectsAsync(true, items)).ContinueWith(t => RefreshCollection());
        }

        private void Recheck(object obj)
        {
            /*if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                Task.Run(async () => await item.ForceReCheckAsync());
            }*/
        }

        private void Redownload(object obj)
        {
            /*var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            var tasks = new List<Task>();
            var forceEnqueue = false;
            var total = items.Length;

            if (total > Settings.Default.MaxParallelDownloads)
            {
                forceEnqueue = true;
            }

            foreach (var item in items)
            {
                if (File.Exists(item.Destination))
                {
                    try
                    {
                        File.Delete(item.Destination);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                }
                if (forceEnqueue)
                {
                    item.Enqueue();
                    QueueProcessor.Add(item);
                }
                else
                {
                    item.Dequeue();
                    QueueProcessor.Remove(item);
                    tasks.Add(item.StartAsync());
                }
            }

            if (forceEnqueue)
            {
                Task.Run(async () => await QueueProcessor.StartAsync());
            }
            else
            {
                Task.Run(async () => await Task.WhenAll(tasks.ToArray()));
            }*/
        }

        private void CopyLinkToClipboard(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            string clipText = string.Empty;
            int counter = 0;
            foreach (var item in items)
            {
                clipText += item.Url;
                if (counter < items.Count - 1)
                {
                    clipText += '\n';
                }
                counter++;
            }
            _clipboardService.SetText(clipText);
        }

        private void ClearFinishedDownloads(object obj)
        {
            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            _ctsUpdatingList = new CancellationTokenSource();

            var items = (from item in DownloadItemsList where item.IsCompleted select item).ToArray();
            Task.Run(async () => await RemoveObjectsAsync(false, items)).ContinueWith(t => RefreshCollection());
        }

        private void CancelBackgroundTask(object obj)
        {
            _ctsUpdatingList?.Cancel();
        }

        private bool CancelBackgroundTask_CanExecute(object obj)
        {
            return this.IsBackgroundWorking;
        }

        private void CheckForUpdates(object obj)
        {
            Task.Run(async () => await TriggerUpdateCheckAsync());
        }

        private void UIClosed(object obj)
        {
            if (_resetAllSettingsOnClose)
            {
                CommonFunctions.ResetAllSettings();
            }
        }

        #endregion

        #region Public methods

        public bool OnClosing()
        {
            if (_ctsUpdatingList != null)
            {
                if (_displayMessage.Invoke(
                    "Background operation in progress. Cancel and exit program?", "Exit",
                    MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.No)
                {
                    return false;
                }
                _ctsUpdatingList.Cancel();
            }

            this.Status = "Saving data...";
            RaisePropertyChanged(nameof(this.Status));

            if (QueueProcessor.IsBusy)
            {
                QueueProcessor.Stop();
            }

            Task<bool> closingTask = Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();

                try
                {
                    Directory.CreateDirectory(AppPaths.LocalAppDataFolder);
                    var writer = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    var list = new SerializableDownloaderObjectModelList();
                    var index = 0;

                    foreach (var item in DownloadItemsList)
                    {
                        if (item.IsDownloading)
                        {
                            await item.PauseAsync();
                        }
                        else if (item.IsCompleted && Settings.Default.ClearFinishedOnExit)
                        {
                            continue;
                        }

                        var sItem = new SerializableDownloaderObjectModel
                        {
                            Index = index++,
                            Url = item.Url,
                            Destination = item.Destination,
                            TotalBytesToDownload = item.TotalBytesToDownload,
                            IsQueued = QueueProcessor.IsQueued(item),
                            Status = item.Status,
                            DateCreated = item.DateCreated,
                            StatusCode = item.StatusCode
                        };

                        list.Objects.Add(sItem);
                    }

                    using var streamWriter = new StreamWriter(AppPaths.DownloadsHistoryFile, false);
                    writer.Serialize(streamWriter, list);
                }
                catch
                {
                    // close even when an exception occurs
                }
                finally
                {
                    _semaphoreUpdatingList.Release();
                }

                Settings.Default.Save();
                return true;
            });

            return closingTask.GetAwaiter().GetResult();
        }

        #endregion

        #region Private methods

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private void SwitchCategory(Category category)
        {
            switch (category)
            {
                case Category.All:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        return true;
                    });
                    break;

                case Category.Downloading:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.IsDownloading;
                    });
                    break;

                case Category.Finished:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Finished;
                    });
                    break;

                case Category.Paused:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Paused;
                    });
                    break;

                case Category.Queued:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return QueueProcessor.IsQueued(item);
                    });
                    break;

                case Category.Ready:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Ready && !QueueProcessor.IsQueued(item);
                    });
                    break;

                case Category.Errored:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Errored;
                    });
                    break;
            }

            Settings.Default.LastSelectedCatagory = category.ToString();
        }

        private void RefreshCollection()
        {
            if (_semaphoreUpdatingList.CurrentCount == 0)
            {
                return;
            }

            // cancel all pending refreshes
            Monitor.Enter(_lockCtsRefreshViewList);
            foreach (var oldCts in _ctsRefreshViewList)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            _ctsRefreshViewList.Clear();
            var newCts = new CancellationTokenSource();
            _ctsRefreshViewList.Add(newCts);
            Monitor.Exit(_lockCtsRefreshViewList);

            Task.Run(async () =>
            {
                var semTask = _semaphoreRefreshingView.WaitAsync(newCts.Token);
                var throttle = Task.Delay(_collectionRefreshDelay);
                try
                {
                    await semTask;
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CollectionView.Refresh();
                        CommandManager.InvalidateRequerySuggested();
                    });
                    await throttle;
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    if (semTask.IsCompletedSuccessfully)
                    {
                        _semaphoreRefreshingView.Release();
                    }
                }
            }, newCts.Token);
        }

        private async Task AddItemsAsync(string destination, bool enqueue, bool start = false, params string[] urls)
        {
            int total = urls.Length;
            var existingUrls = (from di in DownloadItemsList select di.Url).ToArray();
            var existingDestinations = (from di in DownloadItemsList select di.Destination).ToArray();
            var itemsToAdd = new DownloaderObjectModel[total];
            List<IQueueable> itemsToEnqueue = new();
            List<string> skipping = new();
            var wasCanceled = false;
            var ct = _ctsUpdatingList.Token;

            await _semaphoreUpdatingList.WaitAsync();
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            for (int i = 0; i < total; i++)
            {
                int progress = (int)((double)(i + 1) / total * 100);
                this.Progress = progress;
                this.Status = "Creating download " + (i + 1) + " of " + total + ": " + urls[i];
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.Progress));

                if (existingUrls.Contains(urls[i]))
                {
                    skipping.Add(urls[i]);
                    continue;
                }
                var fileName = CommonFunctions.GetFreshFilename(destination + CommonFunctions.GetFilenameFromUrl(urls[i]));
                if (existingDestinations.Contains(fileName))
                {
                    skipping.Add(urls[i]);
                    continue;
                }

                DownloaderObjectModel item;
                item = new DownloaderObjectModel(
                        _client,
                        urls[i],
                        fileName,
                        Download_Created,
                        Download_Started,
                        Download_Stopped,
                        Download_PropertyChanged,
                        _progressReporter);

                itemsToAdd[i] = item;

                if (enqueue)
                {
                    // enqueue lazily because we might cancel the process
                    // before the items appear in the downloads list
                    itemsToEnqueue.Add(item);
                }

                if (ct.IsCancellationRequested)
                {
                    wasCanceled = true;
                    break;
                }
            }

            if (!wasCanceled)
            {
                this.Status = "Listing...";
                RaisePropertyChanged(nameof(this.Status));

                AddObjects(itemsToAdd);
                QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
            }

            _ctsUpdatingList = null;
            this.Progress = 0;
            this.Status = "Ready";
            RaisePropertyChanged(nameof(this.Progress));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            _semaphoreUpdatingList.Release();

            RefreshCollection();

            if (!wasCanceled)
            {
                if (skipping.Count > 0)
                {
                    _showUrls(
                        skipping,
                        "Duplicate Entries",
                        "The following URLs were not added because they are already in the list:");
                }

                if (start)
                {
                    await StartDownloadAsync(enqueue, itemsToAdd);
                }
            }
        }

        private void AddObjects(params DownloaderObjectModel[] objects)
        {
            Monitor.Enter(_lockDownloadItemsList);
            int total = objects.Length;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        if (objects[i] == null) continue;

                        DownloadItemsList.Add(objects[i]);
                    }
                });
            }
            catch
            {
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        private async Task RemoveObjectsAsync(bool delete, params DownloaderObjectModel[] items)
        {
            var ct = _ctsUpdatingList.Token;
            int progress;
            string primaryStatus = "Preparing to remove ";

            // items must be dequeued before removal/deletion otherwise,
            // the QueueProcessor may start processing items marked for removal

            await _semaphoreUpdatingList.WaitAsync();

            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            // remove the items from the download and queue lists

            this.Status = "Delisting...";
            RaisePropertyChanged(nameof(this.Status));

            QueueProcessor.Dequeue(items);

            Application.Current.Dispatcher.Invoke(() =>
            {
                Monitor.Enter(_lockDownloadItemsList);
                if (items.Length == DownloadItemsList.Count)
                {
                    DownloadItemsList.Clear();
                }
                else
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        DownloadItemsList.Remove(items[i]);
                    }
                }
                Monitor.Exit(_lockDownloadItemsList);
            });

            // remove the items from the file system

            if (!delete)
            {
                primaryStatus = "Cleaning up ";
            }
            else
            {
                primaryStatus = "Deleting ";
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;

                progress = (int)((double)(i + 1) / items.Length * 100);
                this.Status = primaryStatus + (i + 1) + " of " + items.Length + ": " + items[i].Name;
                this.Progress = progress;
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.Progress));

                if (items[i].IsDownloading)
                {
                    await items[i].CancelAsync();
                }
                else if (delete && items[i].IsCompleted)
                {
                    try
                    {
                        FileSystem.DeleteFile(
                            items[i].Destination,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            _ctsUpdatingList = null;
            this.Progress = 0;
            this.Status = "Ready";
            RaisePropertyChanged(nameof(this.Progress));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            _semaphoreUpdatingList.Release();

            RefreshCollection();
        }

        /// <summary>
        /// Starts downloading the specified items. If the number of items is larger than the
        /// maximum number of parallel downloads allowed, all items are enqueued instead.
        /// </summary>
        /// <param name="enqueue">Whether to enqueue the items instead of immediately starting the download.</param>
        /// <param name="items">The items to download.</param>
        /// <returns></returns>
        private async Task StartDownloadAsync(bool enqueue, params DownloaderObjectModel[] items)
        {
            bool forceEnqueue = enqueue;
            List<IQueueable> itemsToEnqueue = new();
            List<Task> itemsToDownload = new();

            if (items.Length > Settings.Default.MaxParallelDownloads)
            {
                forceEnqueue = true;
            }

            foreach (var item in items)
            {
                if (forceEnqueue)
                {
                    itemsToEnqueue.Add(item);
                }
                else
                {
                    itemsToDownload.Add(Task.Run(async () => await item.StartAsync()));
                }
            }

            if (forceEnqueue)
            {
                QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                await QueueProcessor.StartAsync();
            }
            else
            {
                await Task.WhenAll(itemsToDownload);
            }
        }

        private void StartReportingSpeed()
        {
            if (_semaphoreMeasuringSpeed.CurrentCount == 0) return;

            Task.Run(async () =>
            {
                await _semaphoreMeasuringSpeed.WaitAsync();
                var stopWatch = new Stopwatch();
                long bytesFrom;
                long bytesTo;
                long bytesCaptured;
                do
                {
                    stopWatch.Restart();
                    bytesFrom = this.BytesDownloadedThisSession;
                    await Task.Delay(1000);
                    bytesTo = this.BytesDownloadedThisSession;
                    stopWatch.Stop();
                    bytesCaptured = bytesTo - bytesFrom;

                    if (bytesCaptured >= 0)
                    {
                        this.Speed = (long)(bytesCaptured / ((double)stopWatch.ElapsedMilliseconds / 1000));
                        RaisePropertyChanged(nameof(this.Speed));
                        RaisePropertyChanged(nameof(this.BytesDownloadedThisSession));
                    }
                } while (this.DownloadingCount > 0);

                this.Speed = null;
                RaisePropertyChanged(nameof(this.Speed));
                _semaphoreMeasuringSpeed.Release();
            });
        }

        private void ShowBusyMessage()
        {
            _displayMessage.Invoke("Operation in progress. Please wait.");
        }

        private async Task TriggerUpdateCheckAsync(bool silent = false)
        {
            string url = await AppUpdateService.GetUpdateUrl(
                   AppConstants.UpdateLink,
                    Assembly.GetExecutingAssembly().GetName().Name,
                    Assembly.GetExecutingAssembly().GetName().Version.ToString());

            if (string.IsNullOrEmpty(url))
            {
                if (!silent)
                {
                    _displayMessage.Invoke(
                        "No new updates are available.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (_displayMessage.Invoke(
                "An update is available.\n\nWould you like to download it now?", "Update",
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes) == MessageBoxResult.Yes)
            {
                Process.Start("explorer.exe", url);
            }
        }

        #endregion

        #region Event handlers

        private void QueueProcessor_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void QueueProcessor_Stopped(object sender, EventArgs e)
        {
        }

        private void QueueProcessor_Started(object sender, EventArgs e)
        {
        }

        private void QueueProcessor_ItemEnqueued(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void QueueProcessor_ItemDequeued(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void Download_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void Download_Created(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void Download_Started(object sender, EventArgs e)
        {
            RefreshCollection();
            StartReportingSpeed();
        }

        private void Download_Stopped(object sender, EventArgs e)
        {
            if (this.DownloadingCount == 0)
            {
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Status));

                if (this.QueuedCount == 0)
                {
                    this.QueueProcessor.Stop();
                }
            }

            RefreshCollection();

            Monitor.Enter(_lockBytesTransferredOverLifetime);
            try
            {
                Settings.Default.BytesTransferredOverLifetime +=
                    (ulong)(sender as DownloaderObjectModel).BytesDownloadedThisSession;
            }
            finally
            {
                Monitor.Exit(_lockBytesTransferredOverLifetime);
            }
        }

        private void DownloadItemsList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
        }

        private void CollectionView_CurrentChanged(object sender, EventArgs e)
        {
            this.Count = DownloadItemsList.Count;
            this.QueuedCount = QueueProcessor.Count;
            this.ReadyCount = DownloadItemsList.Count(o => o.Status == DownloadStatus.Ready && !QueueProcessor.IsQueued(o));
            this.DownloadingCount = DownloadItemsList.Count(o => o.Status == DownloadStatus.Downloading);
            this.PausedCount = DownloadItemsList.Count(o => o.Status == DownloadStatus.Paused);
            this.FinishedCount = DownloadItemsList.Count(o => o.Status == DownloadStatus.Finished);
            this.ErroredCount = DownloadItemsList.Count(o => o.Status == DownloadStatus.Errored);

            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.DownloadingCount));
            RaisePropertyChanged(nameof(this.ErroredCount));
            RaisePropertyChanged(nameof(this.FinishedCount));
            RaisePropertyChanged(nameof(this.PausedCount));
            RaisePropertyChanged(nameof(this.QueuedCount));
            RaisePropertyChanged(nameof(this.ReadyCount));
            RaisePropertyChanged(nameof(this.IsDownloading));

            if (!this.IsBackgroundWorking)
            {
                if (this.DownloadingCount > 0)
                {
                    this.Status = this.DownloadingCount + " item(s) downloading";
                }
                else
                {
                    if (_semaphoreUpdatingList.CurrentCount > 0)
                    {
                        this.Status = "Ready";
                    }
                }

                RaisePropertyChanged(nameof(this.Status));
            }
        }

        #endregion    
    }
}