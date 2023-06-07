// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.Helpers;
using AMDownloader.ObjectModel;
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
        All, Ready, Queued, Downloading, Paused, Finished, Errored, Verifying
    }

    internal class DownloaderViewModel : INotifyPropertyChanged, IClosing
    {
        #region Fields

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
        private HttpClient _client;
        private readonly ShowUrlsDelegate _showUrls;

        #endregion Fields

        #region Properties

        public ObservableCollection<DownloaderObjectModel> DownloadItemsList { get; }
        public ObservableCollection<Category> CategoriesList { get; }
        public ICollectionView CollectionView { get; }
        public QueueProcessor QueueProcessor { get; }
        public int Progress { get; private set; }
        public long BytesDownloaded { get; private set; }
        public long? Speed { get; private set; }
        public int Count { get; private set; }
        public int ReadyCount { get; private set; }
        public int DownloadingCount { get; private set; }
        public int QueuedCount { get; private set; }
        public int FinishedCount { get; private set; }
        public int ErroredCount { get; private set; }
        public int PausedCount { get; private set; }
        public int VerifyingCount { get; private set; }
        public bool IsBackgroundWorking => _ctsUpdatingList != null;
        public bool IsDownloading => DownloadingCount > 0;
        public string Status { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public IProgress<long> ProgressReporter;

        #endregion Properties

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
        public ICommand ShowHelpTopicsCommand { get; private set; }

        #endregion Commands

        #region Constructors

        public DownloaderViewModel(DisplayMessageDelegate displayMessage, ShowUrlsDelegate showUrls)
        {
            DownloadItemsList = new();
            DownloadItemsList.CollectionChanged += DownloadItemsList_CollectionChanged;
            CategoriesList = new ObservableCollection<Category>();
            QueueProcessor = new QueueProcessor(Settings.Default.MaxParallelDownloads, QueueProcessor_PropertyChanged);
            CollectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            CollectionView.CurrentChanged += CollectionView_CurrentChanged;
            this.Count = 0;
            this.DownloadingCount = 0;
            this.ErroredCount = 0;
            this.FinishedCount = 0;
            this.PausedCount = 0;
            this.QueuedCount = 0;
            this.ReadyCount = 0;
            this.VerifyingCount = 0;
            this.BytesDownloaded = 0;
            this.Status = "Ready";
            this.ProgressReporter = new Progress<long>(value =>
            {
                Monitor.Enter(_lockBytesDownloaded);
                try
                {
                    this.BytesDownloaded += value;
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
            _lockBytesDownloaded = this.BytesDownloaded;
            _lockBytesTransferredOverLifetime = Settings.Default.BytesTransferredOverLifetime;
            _lockCtsRefreshViewList = _ctsRefreshViewList;
            _showUrls = showUrls;

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
            ShowHelpTopicsCommand = new RelayCommand<object>(ShowHelpTopics);

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
            Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();

                _ctsUpdatingList = new CancellationTokenSource();
                var ct = _ctsUpdatingList.Token;

                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                try
                {
                    if (Directory.Exists(AppPaths.LocalAppData))
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
                    var finalObjects = new DownloaderObjectModel[sourceObjects.Length];
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
                            ref _client,
                            sourceObjects[i].Url,
                            sourceObjects[i].Destination,
                            sourceObjects[i].IsQueued,
                            sourceObjects[i].TotalBytesToDownload,
                            sourceObjects[i].StatusCode,
                            Download_Created,
                            Download_Verifying,
                            Download_Verified,
                            Download_Started,
                            Download_Stopped,
                            Download_Enqueued,
                            Download_Dequeued,
                            Download_Finished,
                            Download_PropertyChanged,
                            ProgressReporter,
                            ref _requestThrottler);
                        item.SetCreationTime(sourceObjects[i].DateCreated);

                        finalObjects[i] = item;
                    }

                    this.Status = "Listing...";
                    RaisePropertyChanged(nameof(this.Status));

                    AddObjects(finalObjects);
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

        #endregion Constructors

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
                        return item.IsBeingDownloaded;
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
                        return item.IsQueued;
                    });
                    break;

                case Category.Ready:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Ready;
                    });
                    break;

                case Category.Errored:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Error;
                    });
                    break;

                case Category.Verifying:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.Status == DownloadStatus.Verifying;
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
            var removeCtsList = new List<CancellationTokenSource>();
            foreach (var oldCts in _ctsRefreshViewList)
            {
                oldCts.Cancel();
                removeCtsList.Add(oldCts);
            }
            foreach (var oldCts in removeCtsList)
            {
                _ctsRefreshViewList.Remove(oldCts);
                oldCts.Dispose();
            }
            var newCts = new CancellationTokenSource();
            _ctsRefreshViewList.Add(newCts);
            Monitor.Exit(_lockCtsRefreshViewList);

            Task.Run(async () =>
            {
                var semTask = _semaphoreRefreshingView.WaitAsync(newCts.Token);
                var throttle = Task.Delay(1000);
                try
                {
                    await semTask;
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CollectionView.Refresh();
                        CommandManager.InvalidateRequerySuggested();
                    });
                    await throttle; // provide max 1 refresh per sec
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
            await _semaphoreUpdatingList.WaitAsync();

            _ctsUpdatingList = new CancellationTokenSource();
            var ct = _ctsUpdatingList.Token;
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            int total = urls.Length;
            int maxParallelDownloads = Settings.Default.MaxParallelDownloads;
            var items = new DownloaderObjectModel[urls.Length];
            var tasks = new List<Task>();
            var forceEnqueue = false;
            var existingUrls = (from di in DownloadItemsList select di.Url).ToArray();
            var existingDestinations = (from di in DownloadItemsList select di.Destination).ToArray();
            List<string> skipping = new List<string>();
            var wasCanceled = false;

            if (start && !enqueue && urls.Length > Settings.Default.MaxParallelDownloads)
            {
                forceEnqueue = true;
            }

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
                var fileName = CommonFunctions.GetFreshFilename(destination + Path.GetFileName(urls[i]));
                if (existingDestinations.Contains(fileName))
                {
                    skipping.Add(urls[i]);
                    continue;
                }

                DownloaderObjectModel item;
                if (forceEnqueue || enqueue)
                {
                    item = new DownloaderObjectModel(
                        ref _client,
                        urls[i],
                        fileName,
                        enqueue: true,
                        Download_Created,
                        Download_Verifying,
                        Download_Verified,
                        Download_Started,
                        Download_Stopped,
                        Download_Enqueued,
                        Download_Dequeued,
                        Download_Finished,
                        Download_PropertyChanged,
                        ProgressReporter,
                        ref _requestThrottler);
                }
                else
                {
                    item = new DownloaderObjectModel(
                        ref _client,
                        urls[i],
                        fileName,
                        enqueue: false,
                        Download_Created,
                        Download_Verifying,
                        Download_Verified,
                        Download_Started,
                        Download_Stopped,
                        Download_Enqueued,
                        Download_Dequeued,
                        Download_Finished,
                        Download_PropertyChanged,
                        ProgressReporter,
                        ref _requestThrottler);
                    if (start)
                    {
                        tasks.Add(item.StartAsync());
                    }
                }
                items[i] = item;

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

                AddObjects(items);
            }

            _ctsUpdatingList = null;

            this.Progress = 0;
            this.Status = "Ready";
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            _semaphoreUpdatingList.Release();

            RefreshCollection();

            if (!wasCanceled)
            {
                if (skipping.Count > 0)
                {
                    _showUrls(
                        skipping, "Duplicate Entries",
                        "The following URLs were not added because they are already in the list:");
                }

                if ((enqueue && start) || forceEnqueue)
                {
                    await QueueProcessor.StartAsync();
                }
                else
                {
                    await Task.WhenAll(tasks);
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

                        if (objects[i].IsQueued)
                        {
                            this.QueueProcessor.Add(objects[i]);
                        }
                    }
                });
            }
            catch { }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        private async Task RemoveObjectsAsync(bool delete, params DownloaderObjectModel[] objects)
        {
            await _semaphoreUpdatingList.WaitAsync();

            var dequeueThese = new List<IQueueable>();
            var itemsProcessed = new List<DownloaderObjectModel>();
            var total = objects.Length;
            _ctsUpdatingList = new CancellationTokenSource();
            var ct = _ctsUpdatingList.Token;
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));
            int progress;
            string primaryStatus = "Removing ";
            if (delete) primaryStatus = "Deleting ";
            for (int i = 0; i < total; i++)
            {
                progress = (int)((double)(i + 1) / total * 100);
                this.Status = primaryStatus + (i + 1) + " of " + total + ": " + objects[i].Name;
                this.Progress = progress;
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.Progress));

                if (objects[i] == null) continue;

                if (objects[i].IsQueued)
                {
                    objects[i].Dequeue();
                    dequeueThese.Add(objects[i]);
                }

                if (objects[i].IsBeingDownloaded)
                {
                    await objects[i].CancelAsync();
                }
                else
                {
                    // delete all UNFINISHED downloads forcefully
                    if (objects[i].Status != DownloadStatus.Finished || delete)
                    {
                        try
                        {
                            if (objects[i].Status == DownloadStatus.Finished)
                            {
                                FileSystem.DeleteFile(
                                    objects[i].Destination,
                                    UIOption.OnlyErrorDialogs,
                                    RecycleOption.SendToRecycleBin);
                            }
                            else
                            {
                                File.Delete(objects[i].TempDestination);
                            }
                        }
                        catch { }
                    }
                }

                itemsProcessed.Add(objects[i]);

                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            this.Status = "Delisting...";
            RaisePropertyChanged(nameof(this.Status));

            Application.Current.Dispatcher.Invoke(() =>
            {
                Monitor.Enter(_lockDownloadItemsList);
                for (int i = 0; i < itemsProcessed.Count; i++)
                {
                    DownloadItemsList.Remove(itemsProcessed[i]);
                }
                Monitor.Exit(_lockDownloadItemsList);
            });

            if (dequeueThese.Count > 0)
            {
                this.Status = "Refreshing queue...";
                RaisePropertyChanged(nameof(this.Status));
                QueueProcessor.Remove(dequeueThese.ToArray());
            }

            _ctsUpdatingList = null;
            this.Status = "Ready";
            this.Progress = 0;
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            // free up memory
            for (int i = 0; i < objects.Length; i++)
            {
                objects[i] = null;
            }

            _semaphoreUpdatingList.Release();
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
                    bytesFrom = 0;
                    bytesTo = 0;
                    stopWatch.Start();
                    bytesFrom = this.BytesDownloaded;
                    await Task.Delay(1000);
                    bytesTo = this.BytesDownloaded;
                    stopWatch.Stop();
                    bytesCaptured = bytesTo - bytesFrom;
                    if (bytesCaptured >= 0 && stopWatch.ElapsedMilliseconds > 0)
                    {
                        this.Speed = (long)((double)bytesCaptured / ((double)stopWatch.ElapsedMilliseconds / 1000));
                        RaisePropertyChanged(nameof(this.Speed));
                        RaisePropertyChanged(nameof(this.BytesDownloaded));
                    }
                    stopWatch.Reset();
                } while (bytesCaptured > 0);
                this.Speed = null;
                RaisePropertyChanged(nameof(this.Speed));
                _semaphoreMeasuringSpeed.Release();
            });
        }

        private void ShowBusyMessage()
        {
            _displayMessage.Invoke("Operation in progress. Please wait.");
        }

        #endregion Private methods

        #region Command methods

        private void CategoryChanged(object obj)
        {
            if (obj == null) return;
            SwitchCategory((Category)obj);
        }

        private void Start(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            var tasks = new List<Task>();
            var forceEnqueue = false;
            var total = items.Length;

            if (total > Settings.Default.MaxParallelDownloads)
            {
                forceEnqueue = true;
            }

            foreach (var item in items)
            {
                if (item.IsBeingDownloaded) continue;
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
            }
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
            var vm = new AddDownloadViewModel(AddItemsAsync, win.Preview, _displayMessage);
            win.DataContext = vm;
            win.Owner = obj as Window;
            win.ShowDialog();
        }

        private void Remove(object obj)
        {
            if (obj == null) return;
            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

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
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Status));
            });
        }

        private void StopQueue(object obj)
        {
            QueueProcessor.Stop();
        }

        private void ShowOptions(object obj)
        {
            var win = new OptionsWindow();
            win.Owner = obj as Window;
            win.ShowDialog();
        }

        private void Enqueue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                item.Enqueue();
                QueueProcessor.Add(item);
            }
        }

        private void Dequeue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                item.Dequeue();
            }
            QueueProcessor.Remove(items);
        }

        private void DeleteFile(object obj)
        {
            if (obj == null) return;

            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            Task.Run(async () => await RemoveObjectsAsync(true, items)).ContinueWith(t => RefreshCollection());
        }

        private void Recheck(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                Task.Run(async () => await item.ForceReCheckAsync());
            }
        }

        private void Redownload(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
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
            }
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

        private void ShowHelpTopics(object obj)
        {
            // Process.Start("explorer.exe", AppConstants.DocLink);
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
            if (QueueProcessor.IsBusy) QueueProcessor.Stop();

            Task<bool> closingTask = Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();
                try
                {
                    Directory.CreateDirectory(AppPaths.LocalAppData);
                    var writer = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    var list = new SerializableDownloaderObjectModelList();
                    var index = 0;
                    foreach (var item in DownloadItemsList)
                    {
                        if (item.IsBeingDownloaded) item.Pause();
                        if (item.Status == DownloadStatus.Finished && Settings.Default.ClearFinishedOnExit) continue;
                        var sItem = new SerializableDownloaderObjectModel
                        {
                            Index = index++,
                            Url = item.Url,
                            Destination = item.Destination,
                            TotalBytesToDownload = item.TotalBytesToDownload,
                            IsQueued = item.IsQueued,
                            IsCompleted = item.IsCompleted,
                            DateCreated = item.DateCreated,
                            StatusCode = item.StatusCode
                        };
                        list.Objects.Add(sItem);
                    }
                    using (var streamWriter = new StreamWriter(AppPaths.DownloadsHistoryFile, false))
                    {
                        writer.Serialize(streamWriter, list);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    _semaphoreUpdatingList.Release();
                }
            }).ContinueWith(t =>
            {
                Settings.Default.Save();
                return t.Result;
            });

            return closingTask.GetAwaiter().GetResult();
        }

        #endregion

        #region Event handlers

        private void DownloadItemsList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {

        }

        private void CollectionView_CurrentChanged(object sender, EventArgs e)
        {
            var items = DownloadItemsList.ToArray();
            int finished = 0;
            int queued = 0;
            int errored = 0;
            int ready = 0;
            int verifying = 0;
            int paused = 0;
            int downloading = 0;
            int total = items.Length;
            foreach (var item in items)
            {
                switch (item.Status)
                {
                    case DownloadStatus.Downloading:
                        downloading++;
                        break;

                    case DownloadStatus.Error:
                        errored++;
                        break;

                    case DownloadStatus.Finished:
                        finished++;
                        break;

                    case DownloadStatus.Paused:
                        paused++;
                        if (item.IsQueued)
                        {
                            queued++;
                        }
                        break;

                    case DownloadStatus.Queued:
                        queued++;
                        break;

                    case DownloadStatus.Ready:
                        ready++;
                        break;

                    case DownloadStatus.Verifying:
                        verifying++;
                        break;
                }
            }
            this.Count = total;
            this.DownloadingCount = downloading;
            this.ErroredCount = errored;
            this.FinishedCount = finished;
            this.PausedCount = paused;
            this.QueuedCount = queued;
            this.ReadyCount = ready;
            this.VerifyingCount = verifying;
            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.DownloadingCount));
            RaisePropertyChanged(nameof(this.ErroredCount));
            RaisePropertyChanged(nameof(this.FinishedCount));
            RaisePropertyChanged(nameof(this.PausedCount));
            RaisePropertyChanged(nameof(this.QueuedCount));
            RaisePropertyChanged(nameof(this.ReadyCount));
            RaisePropertyChanged(nameof(this.VerifyingCount));
            RaisePropertyChanged(nameof(this.IsDownloading));
            if (!this.IsBackgroundWorking)
            {
                if (downloading > 0)
                {
                    this.Status = downloading + " item(s) downloading";
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

        private void QueueProcessor_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void Download_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void Download_Created(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void Download_Verifying(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void Download_Verified(object sender, EventArgs e)
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

        private void Download_Enqueued(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void Download_Dequeued(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        private void Download_Finished(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        #endregion
    }
}