// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.ObjectModel;
using AMDownloader.ObjectModel.Serializable;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using AMDownloader.RequestThrottling;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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

    internal delegate MessageBoxResult DisplayMessageDelegate(string message, string title = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information, MessageBoxResult defaultResult = MessageBoxResult.OK);

    public enum Categories
    {
        All, Ready, Queued, Downloading, Paused, Finished, Errored, Verifying
    }

    internal class DownloaderViewModel : INotifyPropertyChanged
    {
        #region Fields

        private readonly DisplayMessageDelegate _displayMessage;
        private readonly ClipboardObserver _clipboardService;
        private readonly object _lockDownloadItemsList;
        private readonly object _lockBytesDownloaded;
        private readonly object _lockBytesTransferredOverLifetime;
        private readonly SemaphoreSlim _semaphoreMeasuringSpeed;
        private readonly SemaphoreSlim _semaphoreUpdatingList;
        private readonly SemaphoreSlim _semaphoreRefreshingView;
        private CancellationTokenSource _ctsUpdatingList;
        private CancellationTokenSource _ctsRefreshView;
        private RequestThrottler _requestThrottler;
        private HttpClient _client;

        #endregion Fields

        #region Properties

        public ObservableCollection<DownloaderObjectModel> DownloadItemsList { get; }
        public ObservableCollection<Categories> CategoriesList { get; }
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
        public ICommand RemoveFromListCommand { private get; set; }
        public ICommand CancelCommand { private get; set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand OpenContainingFolderCommand { get; private set; }
        public ICommand StartQueueCommand { get; private set; }
        public ICommand StopQueueCommand { get; private set; }
        public ICommand CloseAppCommand { get; private set; }
        public ICommand CategoryChangedCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand EnqueueCommand { get; private set; }
        public ICommand DequeueCommand { get; private set; }
        public ICommand DeleteFileCommand { get; private set; }
        public ICommand RecheckCommand { get; private set; }
        public ICommand CopyLinkToClipboardCommand { get; private set; }
        public ICommand ClearFinishedDownloadsCommand { get; private set; }
        public ICommand CancelBackgroundTaskCommand { get; private set; }

        #endregion Commands

        #region Constructors

        public DownloaderViewModel(DisplayMessageDelegate displayMessage)
        {
            _client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            CategoriesList = new ObservableCollection<Categories>();
            QueueProcessor = new QueueProcessor(Settings.Default.MaxParallelDownloads, QueueProcessor_PropertyChanged);
            _requestThrottler = new RequestThrottler(AppConstants.RequestThrottlerInterval);
            CollectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            CollectionView.CurrentChanged += CollectionView_CurrentChanged;
            _clipboardService = new ClipboardObserver();
            _semaphoreMeasuringSpeed = new SemaphoreSlim(1);
            _semaphoreUpdatingList = new SemaphoreSlim(1);
            _semaphoreRefreshingView = new SemaphoreSlim(1);
            _ctsUpdatingList = null;
            _ctsRefreshView = null;
            _displayMessage = displayMessage;
            _lockDownloadItemsList = DownloadItemsList;
            _lockBytesDownloaded = this.BytesDownloaded;
            _lockBytesTransferredOverLifetime = Settings.Default.BytesTransferredOverLifetime;
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

            AddCommand = new RelayCommand<object>(Add, Add_CanExecute);
            StartCommand = new RelayCommand<object>(Start, Start_CanExecute);
            RemoveFromListCommand = new RelayCommand<object>(RemoveFromList, RemoveFromList_CanExecute);
            CancelCommand = new RelayCommand<object>(Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand<object>(Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand<object>(Open, Open_CanExecute);
            OpenContainingFolderCommand = new RelayCommand<object>(OpenContainingFolder, OpenContainingFolder_CanExecute);
            StartQueueCommand = new RelayCommand<object>(StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand<object>(StopQueue, StopQueue_CanExecute);
            CloseAppCommand = new RelayCommand<object>(CloseApp);
            CategoryChangedCommand = new RelayCommand<object>(CategoryChanged);
            OptionsCommand = new RelayCommand<object>(ShowOptions);
            EnqueueCommand = new RelayCommand<object>(Enqueue, Enqueue_CanExecute);
            DequeueCommand = new RelayCommand<object>(Dequeue, Dequeue_CanExecute);
            DeleteFileCommand = new RelayCommand<object>(DeleteFile, DeleteFile_CanExecute);
            RecheckCommand = new RelayCommand<object>(Recheck, Recheck_CanExecute);
            CopyLinkToClipboardCommand = new RelayCommand<object>(CopyLinkToClipboard, CopyLinkToClipboard_CanExecute);
            ClearFinishedDownloadsCommand = new RelayCommand<object>(ClearFinishedDownloads);
            CancelBackgroundTaskCommand = new RelayCommand<object>(CancelBackgroundTask, CancelBackgroundTask_CanExecute);
            foreach (Categories cat in (Categories[])Enum.GetValues(typeof(Categories)))
            {
                CategoriesList.Add(cat);
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
                    var finalObjects = new DownloaderObjectModel[sourceObjects.Count()];
                    var total = sourceObjects.Count();

                    for (int i = 0; i < sourceObjects.Count(); i++)
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
                catch
                {
                    return;
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

        #region Methods

        internal void CategoryChanged(object obj)
        {
            var category = (Categories)obj;
            switch (category)
            {
                case Categories.All:
                    CollectionView.Filter = new Predicate<object>((o) => { return true; });
                    break;

                case Categories.Downloading:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.IsBeingDownloaded) return true;
                        return false;
                    });
                    break;

                case Categories.Finished:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Finished) return true;
                        return false;
                    });
                    break;

                case Categories.Paused:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Paused) return true;
                        return false;
                    });
                    break;

                case Categories.Queued:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.IsQueued) return true;
                        return false;
                    });
                    break;

                case Categories.Ready:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Ready) return true;
                        return false;
                    });
                    break;

                case Categories.Errored:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Error) return true;
                        return false;
                    });
                    break;

                case Categories.Verifying:
                    CollectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Verifying) return true;
                        return false;
                    });
                    break;
            }
        }

        internal void Start(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            var tasks = new List<Task>();
            var forceEnqueue = false;
            var total = items.Count();

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
                Task.Run(async () => await Task.WhenAll());
            }
        }

        internal bool Start_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            if (items.Count() == 0) return false;
            foreach (var item in items)
            {
                switch (item.Status)
                {
                    case DownloadStatus.Paused:
                    case DownloadStatus.Ready:
                    case DownloadStatus.Queued:
                    case DownloadStatus.Error:
                        return true;
                }
            }
            return false;
        }

        internal void Pause(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                item.Pause();
            }
        }

        internal bool Pause_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            if (items.Count() == 0) return false;
            foreach (var item in items)
            {
                if (item.Status == DownloadStatus.Downloading) return true;
            }
            return false;
        }

        internal void Cancel(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                item.Cancel();
            }
        }

        internal bool Cancel_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            if (items.Count() == 0) return false;
            foreach (var item in items)
            {
                switch (item.Status)
                {
                    case DownloadStatus.Downloading:
                    case DownloadStatus.Paused:
                        return true;
                }
            }
            return false;
        }

        internal void RemoveFromList(object obj)
        {
            if (obj == null) return;
            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await _semaphoreUpdatingList.WaitAsync();

                    _ctsUpdatingList = new CancellationTokenSource();
                    var ct = _ctsUpdatingList.Token;

                    RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                    try
                    {
                        var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
                        List<DownloaderObjectModel> dequeueThese = new List<DownloaderObjectModel>();
                        int total = items.Count();

                        await Application.Current.Dispatcher.Invoke(async () =>
                        {
                            for (int i = 0; i < total; i++)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }

                                this.Status = "Removing " + (i + 1) + " of " + total + ": " + items[i].Name;
                                this.Progress = (int)((double)(i + 1) / total * 100);
                                RaisePropertyChanged(nameof(this.Status));
                                RaisePropertyChanged(nameof(this.Progress));

                                if (items[i].IsBeingDownloaded)
                                {
                                    await Task.Run(async () => await items[i].PauseAsync());
                                }

                                if (items[i].IsQueued)
                                {
                                    items[i].Dequeue();
                                    dequeueThese.Add(items[i]);
                                }

                                Monitor.Enter(_lockDownloadItemsList);
                                DownloadItemsList.Remove(items[i]);
                                Monitor.Exit(_lockDownloadItemsList);
                            }
                        });

                        this.Status = "Dequeueing items...";
                        RaisePropertyChanged(nameof(this.Status));
                        QueueProcessor.Remove(dequeueThese.ToArray());
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                    finally
                    {
                        _semaphoreUpdatingList.Release();
                        _ctsUpdatingList = null;
                        if (ct.IsCancellationRequested)
                        {
                            this.Status = "Cancelling...";
                        }
                        else
                        {
                            this.Status = "Refreshing list...";
                        }
                        RaisePropertyChanged(nameof(this.IsBackgroundWorking));
                        RaisePropertyChanged(nameof(this.FinishedCount));
                        RaisePropertyChanged(nameof(this.Status));

                        RefreshCollection();

                        this.Status = "Ready";
                        this.Progress = 0;
                        RaisePropertyChanged(nameof(this.Status));
                        RaisePropertyChanged(nameof(this.Progress));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            });
        }

        internal bool RemoveFromList_CanExecute(object obj)
        {
            if (obj == null) return false;
            if (_ctsUpdatingList != null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            if (items.Count() == 0) return false;
            return true;
        }

        internal void Add(object obj)
        {
            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }
            Monitor.Enter(_lockDownloadItemsList);
            try
            {
                var win = new AddDownloadWindow();
                var vm = new AddDownloadViewModel(AddItemsAsync, win.Preview);
                win.DataContext = vm;
                win.Owner = obj as Window;
                win.ShowDialog();
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        internal bool Add_CanExecute(object obj)
        {
            if (_ctsUpdatingList != null) return false;
            return true;
        }

        internal void Open(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsFinished = from item in items where item.Status == DownloadStatus.Finished where new FileInfo(item.Destination).Exists select item;
            if (itemsFinished.Count() > 1)
            {
                MessageBoxResult r = MessageBox.Show(
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

        internal bool Open_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsFinished = from item in items where item.Status == DownloadStatus.Finished where new FileInfo(item.Destination).Exists select item;
            if (itemsFinished.Count() > 0) return true;
            return false;
        }

        internal void OpenContainingFolder(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsOpenable = from item in items where File.Exists(item.Destination) || Directory.Exists(Path.GetDirectoryName(item.Destination)) select item;
            if (itemsOpenable.Count() > 1)
            {
                var result = MessageBox.Show("You have selected to open " + items.Count + " folders.\n\n" +
                    "Opening too many folders at the same time may cause the system to crash.\n\nDo you wish to proceed?",
                    "Open Folder", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No);

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

        internal bool OpenContainingFolder_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                if (Directory.Exists(Path.GetDirectoryName(item.Destination)))
                {
                    return true;
                }
            }
            return false;
        }

        internal void StartQueue(object obj)
        {
            Task.Run(async () =>
            {
                await QueueProcessor.StartAsync();
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Status));
            });
        }

        internal bool StartQueue_CanExecute(object obj)
        {
            return !QueueProcessor.IsBusy && QueueProcessor.HasItems && this.QueuedCount > 0 && _ctsUpdatingList == null;
        }

        internal void StopQueue(object obj)
        {
            QueueProcessor.Stop();
        }

        internal bool StopQueue_CanExecute(object obj)
        {
            return QueueProcessor.IsBusy;
        }

        internal void CloseApp(object obj)
        {
            var window = (Window)obj;

            if (_ctsUpdatingList != null)
            {
                if (_displayMessage.Invoke("Background operation in progress. Cancel and exit program?", "Exit", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.No)
                {
                    return;
                }
                window.IsEnabled = false;
                window.Title = "Quitting, please wait...";
                _ctsUpdatingList.Cancel();
            }
            this.Status = "Saving data...";
            RaisePropertyChanged(nameof(this.Status));
            if (QueueProcessor.IsBusy) QueueProcessor.Stop();
            Task t = Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();
                try
                {
                    Directory.CreateDirectory(AppPaths.LocalAppData);
                    XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    SerializableDownloaderObjectModelList list = new SerializableDownloaderObjectModelList();
                    int index = 0;
                    foreach (var item in DownloadItemsList)
                    {
                        if (item.IsBeingDownloaded) item.Pause();
                        if (item.Status == DownloadStatus.Finished && Settings.Default.ClearFinishedOnExit) return;
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
                }
                catch
                {
                    return;
                }
                finally
                {
                    _semaphoreUpdatingList.Release();
                }
            }).ContinueWith(t =>
            {
                Settings.Default.Save();
                try
                {
                    Application.Current?.Dispatcher.Invoke(Application.Current.Shutdown);
                }
                catch
                {
                }
            });
        }

        internal void ShowOptions(object obj)
        {
            var win = new OptionsWindow();
            win.Owner = obj as Window;
            win.ShowDialog();
        }

        internal void Enqueue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                item.Enqueue();
                QueueProcessor.Add(item);
            }
        }

        internal bool Enqueue_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            return (from item in items where !item.IsQueued where !item.IsBeingDownloaded where !item.IsCompleted select item).Count() > 0;
        }

        internal void Dequeue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                item.Dequeue();
            }
            QueueProcessor.Remove(items);
        }

        internal bool Dequeue_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                if (item.IsQueued && !item.IsBeingDownloaded) return true;
            }
            return false;
        }

        internal void DeleteFile(object obj)
        {
            if (obj == null) return;

            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsDeleted = new BlockingCollection<DownloaderObjectModel>();
            var total = items.Count();

            Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();

                _ctsUpdatingList = new CancellationTokenSource();
                var ct = _ctsUpdatingList.Token;

                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                for (int i = 0; i < total; i++)
                {
                    int progress = (int)((double)(i + 1) / total * 100);
                    this.Progress = progress;
                    this.Status = "Deleting " + (i + 1) + " of " + total + ": " + items[i].Name;
                    RaisePropertyChanged(nameof(this.Status));
                    RaisePropertyChanged(nameof(this.Progress));

                    if (items[i].IsBeingDownloaded)
                    {
                        await Task.Run(async () => await items[i].CancelAsync());
                    }
                    else
                    {
                        if (File.Exists(items[i].Destination))
                        {
                            try
                            {
                                FileSystem.DeleteFile(items[i].Destination, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }

                    itemsDeleted.Add(items[i]);

                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (!ct.IsCancellationRequested)
                {
                    this.Status = "Refreshing list...";
                }
                else
                {
                    this.Status = "Cancelling...";
                }
                RaisePropertyChanged(nameof(this.Status));

                Monitor.Enter(_lockDownloadItemsList);
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var item in itemsDeleted)
                        {
                            DownloadItemsList.Remove(item);
                            item.Dequeue();
                        }
                    });
                    QueueProcessor.Remove(itemsDeleted.ToArray());
                }
                finally
                {
                    Monitor.Exit(_lockDownloadItemsList);
                    _semaphoreUpdatingList.Release();
                    _ctsUpdatingList = null;

                    this.Status = "Ready";
                    this.Progress = 0;
                    RaisePropertyChanged(nameof(this.Progress));
                    RaisePropertyChanged(nameof(this.Status));
                    RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                    RefreshCollection();
                }
            });
        }

        internal bool DeleteFile_CanExecute(object obj)
        {
            if (obj == null) return false;
            if (_semaphoreUpdatingList.CurrentCount == 0) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            return items.Count > 0;
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

        private bool Recheck_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            foreach (var item in items)
            {
                if (!item.IsBeingDownloaded) return true;
            }
            return false;
        }

        internal void CopyLinkToClipboard(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            string clipText = String.Empty;
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

        internal bool CopyLinkToClipboard_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            return items.Count() > 0;
        }

        internal void ClearFinishedDownloads(object obj)
        {
            if (_ctsUpdatingList != null)
            {
                ShowBusyMessage();
                return;
            }
            _semaphoreUpdatingList.Wait();
            Monitor.Enter(_lockDownloadItemsList);
            try
            {
                var itemsFinished = (from item in DownloadItemsList where item.Status == DownloadStatus.Finished select item).ToArray<DownloaderObjectModel>();
                foreach (var item in itemsFinished)
                {
                    DownloadItemsList.Remove(item);
                    if (item.IsQueued) this.QueueProcessor.Remove(item);
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                _semaphoreUpdatingList.Release();
                Task.Run(() => RefreshCollection());
                RaisePropertyChanged(nameof(this.FinishedCount));
            }
        }

        internal bool ClearFinishedDownloads_CanExecute(object obj)
        {
            return _semaphoreUpdatingList.CurrentCount > 0;
        }

        internal void QueueProcessor_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        internal void Download_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        internal void Download_Created(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        internal void Download_Verifying(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        internal void Download_Verified(object sender, EventArgs e)
        {
            Debug.WriteLine("Verified: " + (sender as DownloaderObjectModel).Name);
            RefreshCollection();
        }

        internal void Download_Started(object sender, EventArgs e)
        {
            Debug.WriteLine("Starting: " + (sender as DownloaderObjectModel).Name);
            RefreshCollection();
            StartReportingSpeed();
        }

        internal void Download_Stopped(object sender, EventArgs e)
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
                Settings.Default.BytesTransferredOverLifetime += (ulong)(sender as DownloaderObjectModel).BytesDownloadedThisSession;
            }
            finally
            {
                Monitor.Exit(_lockBytesTransferredOverLifetime);
            }
        }

        internal void Download_Enqueued(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        internal void Download_Dequeued(object sender, EventArgs e)
        {
            RefreshCollection();
        }

        internal void Download_Finished(object sender, EventArgs e)
        {
            RefreshCollection();
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
            int total = items.Count();
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
            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.DownloadingCount));
            RaisePropertyChanged(nameof(this.ErroredCount));
            RaisePropertyChanged(nameof(this.FinishedCount));
            RaisePropertyChanged(nameof(this.PausedCount));
            RaisePropertyChanged(nameof(this.QueuedCount));
            RaisePropertyChanged(nameof(this.ReadyCount));
            RaisePropertyChanged(nameof(this.VerifyingCount));
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.IsDownloading));
        }

        internal void RefreshCollection()
        {
            if (_semaphoreUpdatingList.CurrentCount == 0)
            {
                return;
            }

            _ctsRefreshView?.Cancel();

            Task.Run(async () =>
            {
                _ctsRefreshView = new CancellationTokenSource();
                try
                {
                    var ct = _ctsRefreshView.Token;
                    await _semaphoreRefreshingView.WaitAsync(ct);
                    var throttler = Task.Delay(1000);
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CollectionView.Refresh();
                        CommandManager.InvalidateRequerySuggested();
                    });
                    await throttler;
                    _semaphoreRefreshingView.Release();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    _ctsRefreshView = null;
                }
            });
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private async Task AddItemsAsync(string destination, bool enqueue, bool start = false, params string[] urls)
        {
            await _semaphoreUpdatingList.WaitAsync();

            _ctsUpdatingList = new CancellationTokenSource();
            var ct = _ctsUpdatingList.Token;
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            int total = urls.Count();
            int maxParallelDownloads = Settings.Default.MaxParallelDownloads;
            var items = new DownloaderObjectModel[urls.Count()];
            var tasks = new List<Task>();
            var forceEnqueue = false;
            var existingUrls = (from di in DownloadItemsList select di.Url).ToArray();
            var existingDestinations = (from di in DownloadItemsList select di.Destination).ToArray();
            List<string> skipping = new List<string>();
            var wasCanceled = false;

            if (start && !enqueue && urls.Count() > Settings.Default.MaxParallelDownloads)
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
                    item = new DownloaderObjectModel(ref _client, urls[i], fileName, enqueue: true, Download_Created, Download_Verifying, Download_Verified, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, ProgressReporter, ref _requestThrottler);
                }
                else
                {
                    item = new DownloaderObjectModel(ref _client, urls[i], fileName, enqueue: false, Download_Created, Download_Verifying, Download_Verified, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, ProgressReporter, ref _requestThrottler);
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
                    if (skipping.Count < 100)
                    {
                        _displayMessage("The following URLs were skipped because they are already in the list:\n\n" + string.Join('\n', skipping), "Duplicate URLs");
                    }
                    else
                    {
                        _displayMessage(skipping.Count + " URLs were skipped because they are already in the list.", "Duplicate URLs");
                    }
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
            int total = objects.Count();
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

        private void RemoveObjects(bool delete, params DownloaderObjectModel[] items)
        {
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

        internal void CancelBackgroundTask(object obj)
        {
            _ctsUpdatingList?.Cancel();
        }

        internal bool CancelBackgroundTask_CanExecute(object obj)
        {
            return this.IsBackgroundWorking;
        }

        #endregion Methods
    }
}