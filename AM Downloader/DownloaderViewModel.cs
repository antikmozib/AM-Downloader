using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Xml.Serialization;
using Microsoft.VisualBasic.FileIO;
using AMDownloader.Properties;
using static AMDownloader.SerializableModels;
using static AMDownloader.DownloaderObjectModel;
using static AMDownloader.Common;

namespace AMDownloader
{
    delegate Task AddItemsAsync(string destination, bool enqueue, bool start, params string[] urls);
    class DownloaderViewModel : INotifyPropertyChanged
    {
        #region Fields
        private const int COLLECTION_REFRESH_INTERVAL = 250;
        private readonly ICollectionView _collectionView;
        private ClipboardObserver _clipboardService;
        private object _lockDownloadItemsList;
        private object _lockBytesDownloaded;
        private SemaphoreSlim _semaphoreMeasuringSpeed;
        private SemaphoreSlim _semaphoreUpdatingList;
        private SemaphoreSlim _semaphoreRefreshingView;
        private CancellationTokenSource _ctsUpdatingList;
        #endregion // Fields

        #region Properties
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
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
        public string Status { get; private set; }
        public HttpClient Client;
        public ObservableCollection<Categories> CategoriesList;
        public QueueProcessor QueueProcessor;
        public event PropertyChangedEventHandler PropertyChanged;
        public enum Categories
        {
            All, Ready, Queued, Downloading, Paused, Finished, Errored, Verifying
        }
        public AddItemsAsync AddItemsAsyncDelegate;
        public IProgress<long> ProgressReporter;
        #endregion // Properties

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
        #endregion // Commands

        #region Constructors
        public DownloaderViewModel()
        {
            Client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            CategoriesList = new ObservableCollection<Categories>();
            QueueProcessor = new QueueProcessor(Settings.Default.MaxParallelDownloads, RefreshCollection);
            _collectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            _collectionView.CurrentChanged += CollectionView_CurrentChanged;
            _clipboardService = new ClipboardObserver();
            _lockDownloadItemsList = DownloadItemsList;
            _lockBytesDownloaded = this.BytesDownloaded;
            _semaphoreMeasuringSpeed = new SemaphoreSlim(1);
            _semaphoreUpdatingList = new SemaphoreSlim(1);
            _semaphoreRefreshingView = new SemaphoreSlim(1);
            _ctsUpdatingList = null;
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
            this.AddItemsAsyncDelegate = new AddItemsAsync(AddItemsAsync);
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

            AddCommand = new RelayCommand<object>(Add);
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

            foreach (Categories cat in (Categories[])Enum.GetValues(typeof(Categories)))
            {
                CategoriesList.Add(cat);
            }

            // Populate history
            if (Directory.Exists(ApplicationPaths.LocalAppData))
            {
                this.Status = "Restoring data...";
                RaisePropertyChanged(nameof(this.Status));
                Task.Run(() =>
                {
                    SerializableDownloaderObjectModelList source;
                    var xmlReader = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    try
                    {
                        using (var streamReader = new StreamReader(ApplicationPaths.DownloadsHistoryFile))
                        {
                            source = (SerializableDownloaderObjectModelList)xmlReader.Deserialize(streamReader);
                        }
                        var sourceObjects = source.Objects.ToArray();
                        var finalObjects = new DownloaderObjectModel[sourceObjects.Count()];
                        var total = sourceObjects.Count();
                        for (int i = 0; i < sourceObjects.Count(); i++)
                        {
                            if (sourceObjects[i] == null) continue;
                            int progress = (int)((double)(i + 1) / total * 100);
                            this.Progress = progress;
                            this.Status = "Restoring " + (i + 1) + " of " + total + " (" + progress + "%): " + sourceObjects[i].Url;
                            RaisePropertyChanged(nameof(this.Progress));
                            RaisePropertyChanged(nameof(this.Status));
                            var item = new DownloaderObjectModel(ref Client, sourceObjects[i].Url, sourceObjects[i].Destination, sourceObjects[i].IsQueued, sourceObjects[i].WasCompleted, Download_Verifying, Download_Verified, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, RefreshCollection, ProgressReporter);
                            item.SetCreationTime(sourceObjects[i].DateCreated);
                            finalObjects[i] = item;
                        }
                        AddObjects(finalObjects);
                    }
                    catch
                    {
                        return;
                    }
                    finally
                    {
                        this.Status = "Ready";
                        RaisePropertyChanged(nameof(this.Status));
                    }
                });
            }
        }
        #endregion // Constructors

        #region Methods
        internal void CategoryChanged(object obj)
        {
            var category = (Categories)obj;
            switch (category)
            {
                case Categories.All:
                    _collectionView.Filter = new Predicate<object>((o) => { return true; });
                    break;
                case Categories.Downloading:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.IsBeingDownloaded) return true;
                        return false;
                    });
                    break;
                case Categories.Finished:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Finished) return true;
                        return false;
                    });
                    break;
                case Categories.Paused:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Paused) return true;
                        return false;
                    });
                    break;
                case Categories.Queued:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.IsQueued) return true;
                        return false;
                    });
                    break;
                case Categories.Ready:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Ready) return true;
                        return false;
                    });
                    break;
                case Categories.Errored:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Error) return true;
                        return false;
                    });
                    break;
                case Categories.Verifying:
                    _collectionView.Filter = new Predicate<object>((o) =>
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

            foreach (DownloaderObjectModel item in items)
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
                    tasks.Add(item.StartAsync());
                }
            }

            if (forceEnqueue)
            {
                Task.Run(async () => await QueueProcessor.StartAsync(Settings.Default.MaxConnectionsPerDownload));
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
            foreach (DownloaderObjectModel item in items)
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
            foreach (DownloaderObjectModel item in items)
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

            Monitor.Enter(_lockDownloadItemsList);
            try
            {
                Task.Run(async () =>
                {
                    var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
                    await _semaphoreUpdatingList.WaitAsync();
                    _ctsUpdatingList = new CancellationTokenSource();
                    var ct = _ctsUpdatingList.Token;
                    try
                    {
                        int total = items.Count();
                        for (int i = 0; i < total; i++)
                        {
                            if ((total > 100 && total <= 1000 && (i + 1) % 100 == 0) ||
                                (total > 1000 && (i + 1) % 500 == 0) ||
                                i + 1 == total ||
                                i + 1 == 1 ||
                                total <= 100)
                            {
                                int progress = (int)((double)(i + 1) / total * 100);
                                this.Progress = progress;
                                this.Status = "Removing " + (i + 1) + " of " + total + " (" + progress + "%): " + items[i].Name;
                                RaisePropertyChanged(nameof(this.Status));
                                RaisePropertyChanged(nameof(this.Progress));
                                await Task.Delay(100);
                            }
                            Application.Current.Dispatcher.Invoke(() => DownloadItemsList.Remove(items[i]));
                            if (items[i].IsBeingDownloaded) items[i].Cancel();
                            if (items[i].IsQueued) items[i].Dequeue();
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        _semaphoreUpdatingList.Release();
                        _ctsUpdatingList = null;
                        this.Progress = 0;
                        this.Status = "Ready";
                        RaisePropertyChanged(nameof(this.Progress));
                        RaisePropertyChanged(nameof(this.FinishedCount));
                        RaisePropertyChanged(nameof(this.Status));
                        RefreshCollection();
                    }
                });
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        internal bool RemoveFromList_CanExecute(object obj)
        {
            if (obj == null) return false;
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
                var vm = new AddDownloadViewModel(this);
                var win = new AddDownloadWindow();
                win.DataContext = vm;
                win.Owner = obj as Window;
                win.ShowDialog();
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        internal void Open(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsFinished = from item in items where item.Status == DownloadStatus.Finished where new FileInfo(item.Destination).Exists select item;
            if (itemsFinished.Count() > 5)
            {
                MessageBoxResult r = MessageBox.Show(
                    "You have elected to open " + itemsFinished.Count() + " files. " +
                    "Opening too many files at the same file may cause system freezeups.\n\nDo you wish to proceed?",
                    "Open", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);

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
            var itemsExist = from item in items where new FileInfo(item.Destination).Exists select item;
            foreach (var item in itemsExist)
            {
                Process.Start("explorer.exe", "/select, \"\"" + item.Destination + "\"\"");
            }
        }

        internal bool OpenContainingFolder_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsExist = from item in items where new FileInfo(item.Destination).Exists select item;
            if (itemsExist.Count() > 0) return true;
            return false;
        }

        internal void StartQueue(object obj)
        {
            Task.Run(async () =>
            {
                await QueueProcessor.StartAsync(Settings.Default.MaxConnectionsPerDownload);
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Status));
            });
        }

        internal bool StartQueue_CanExecute(object obj)
        {
            return (!QueueProcessor.IsBusy && QueueProcessor.Count() > 0);
        }

        internal void StopQueue(object obj)
        {
            QueueProcessor.Stop();
        }

        internal bool StopQueue_CanExecute(object obj)
        {
            return (obj != null || QueueProcessor.IsBusy);
        }

        internal void CloseApp(object obj)
        {
            if (_ctsUpdatingList != null)
            {
                _ctsUpdatingList.Cancel();
            }
            this.Status = "Saving data...";
            RaisePropertyChanged(nameof(this.Status));
            Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();
                try
                {
                    if (File.Exists(ApplicationPaths.DownloadsHistoryFile))
                    {
                        File.Delete(ApplicationPaths.DownloadsHistoryFile);
                    }
                    Directory.CreateDirectory(ApplicationPaths.LocalAppData);
                    XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    SerializableDownloaderObjectModelList list = new SerializableDownloaderObjectModelList();
                    foreach (var item in DownloadItemsList)
                    {
                        if (item.IsBeingDownloaded) item.Pause();
                        if (item.Status == DownloadStatus.Finished && Settings.Default.ClearFinishedOnExit) return;
                        var sItem = new SerializableDownloaderObjectModel();
                        sItem.Url = item.Url;
                        sItem.Destination = item.Destination;
                        sItem.IsQueued = item.IsQueued;
                        sItem.WasCompleted = item.IsCompleted;
                        sItem.DateCreated = item.DateCreated;
                        list.Objects.Add(sItem);
                    }
                    using (var streamWriter = new StreamWriter(ApplicationPaths.DownloadsHistoryFile))
                    {
                        writer.Serialize(streamWriter, list);
                    }
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
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
            return (from item in items where !item.IsQueued where !item.IsBeingDownloaded select item).Count() > 0;
        }

        internal void Dequeue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            foreach (var item in items)
            {
                item.Dequeue();
            }
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
            var itemsDeletable = (from item in items where !item.IsBeingDownloaded where File.Exists(item.Destination) select item).ToArray<DownloaderObjectModel>();
            var itemsDeleted = new BlockingCollection<DownloaderObjectModel>();
            var total = itemsDeletable.Count();
            Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();
                _ctsUpdatingList = new CancellationTokenSource();
                var ct = _ctsUpdatingList.Token;
                for (int i = 0; i < total; i++)
                {
                    int progress = (int)((double)(i + 1) / total * 100);
                    this.Progress = progress;
                    this.Status = "Deleting " + (i + 1) + " of " + total + " (" + progress + "%): " + itemsDeletable[i].Name;
                    RaisePropertyChanged(nameof(this.Status));
                    RaisePropertyChanged(nameof(this.Progress));
                    try
                    {
                        FileSystem.DeleteFile(itemsDeletable[i].Destination, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        itemsDeleted.TryAdd(itemsDeletable[i]);
                    }
                    catch
                    {
                        continue;
                    }
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }
                Monitor.Enter(_lockDownloadItemsList);
                this.Status = "Refreshing list...";
                RaisePropertyChanged(nameof(this.Status));
                try
                {
                    foreach (var item in itemsDeleted)
                    {
                        if (item.IsQueued) item.Dequeue();
                        Application.Current.Dispatcher.Invoke(() => DownloadItemsList.Remove(item));
                    }
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
                    RefreshCollection();
                }
            });
        }

        internal bool DeleteFile_CanExecute(object obj)
        {
            if (obj == null) return false;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsDeletable = from item in items where !item.IsBeingDownloaded where File.Exists(item.Destination) select item;
            if (itemsDeletable.Count() > 0) return true;
            return false;
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

        internal bool CopyLinkToClipboard_CanExecute(object obj)
        {
            if (obj == null) return false;
            return true;
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
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                _semaphoreUpdatingList.Release();
                Task.Run(RefreshCollection);
                RaisePropertyChanged(nameof(this.FinishedCount));
            }
        }

        internal void Download_PropertyChanged(object sender, PropertyChangedEventArgs e) { }
        internal void Download_Verifying(object sender, EventArgs e) { }
        internal void Download_Verified(object sender, EventArgs e) { }
        internal void Download_Started(object sender, EventArgs e)
        {
            StartReportingSpeed();
        }
        internal void Download_Stopped(object sender, EventArgs e)
        {
            if (this.DownloadingCount == 0)
            {
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Status));
            }
        }
        internal void Download_Enqueued(object sender, EventArgs e) { }
        internal void Download_Dequeued(object sender, EventArgs e) { }
        internal void Download_Finished(object sender, EventArgs e) { }
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
            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.DownloadingCount));
            RaisePropertyChanged(nameof(this.ErroredCount));
            RaisePropertyChanged(nameof(this.FinishedCount));
            RaisePropertyChanged(nameof(this.PausedCount));
            RaisePropertyChanged(nameof(this.QueuedCount));
            RaisePropertyChanged(nameof(this.ReadyCount));
            RaisePropertyChanged(nameof(this.VerifyingCount));
            RaisePropertyChanged(nameof(this.Status));
        }
        internal void RefreshCollection()
        {
            Task.Run(async () =>
            {
                if (!await _semaphoreRefreshingView.WaitAsync(COLLECTION_REFRESH_INTERVAL))
                {
                    return;
                }
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                Application.Current?.Dispatcher?.Invoke(_collectionView.Refresh);
                stopWatch.Stop();
                if (stopWatch.ElapsedMilliseconds < COLLECTION_REFRESH_INTERVAL)
                {
                    await Task.Delay(COLLECTION_REFRESH_INTERVAL - (int)stopWatch.ElapsedMilliseconds);
                }
                _semaphoreRefreshingView.Release();
            });
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private async Task AddItemsAsync(string destination, bool enqueue, bool start = false, params string[] urls)
        {
            int total = urls.Count();
            int maxParallelDownloads = Settings.Default.MaxParallelDownloads;
            int maxConnectionsPerDownload = Settings.Default.MaxConnectionsPerDownload;
            var items = new DownloaderObjectModel[urls.Count()];
            var tasks = new List<Task>();
            var forceEnqueue = false;

            if (start && !enqueue && urls.Count() > Settings.Default.MaxParallelDownloads)
            {
                forceEnqueue = true;
            }

            for (int i = 0; i < total; i++)
            {
                int progress = (int)((double)(i + 1) / total * 100);
                this.Progress = progress;
                this.Status = "Creating download " + (i + 1) + " of " + total + " (" + progress + "%): " + urls[i];
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.Progress));

                var fileName = GetValidFilename(destination + Path.GetFileName(urls[i]));
                var checkifUrlExists = from di in DownloadItemsList where di.Url == urls[i] select di;
                var checkIfDestinationExists = from di in DownloadItemsList where di.Destination == fileName select di;
                var sameItems = checkIfDestinationExists.Intersect(checkifUrlExists);
                if (sameItems.Count() > 0) continue;

                DownloaderObjectModel item;
                if (forceEnqueue || enqueue)
                {
                    item = new DownloaderObjectModel(ref Client, urls[i], fileName, enqueue: true, completed: false, Download_Verifying, Download_Verified, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, RefreshCollection, ProgressReporter);
                    QueueProcessor.Add(item);
                }
                else
                {
                    item = new DownloaderObjectModel(ref Client, urls[i], fileName, enqueue: false, completed: false, Download_Verifying, Download_Verified, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, RefreshCollection, ProgressReporter);
                    if (start)
                    {
                        tasks.Add(item.StartAsync());
                    }
                }
                items[i] = item;
            }

            this.Progress = 0;
            this.Status = "Ready";
            RaisePropertyChanged(nameof(this.Status));
            RaisePropertyChanged(nameof(this.Progress));

            AddObjects(items);

            if ((enqueue && start) || forceEnqueue)
            {
                await QueueProcessor.StartAsync(maxConnectionsPerDownload);
            }
            else
            {
                await Task.WhenAll(tasks);
            }
        }

        private void AddObjects(params DownloaderObjectModel[] objects)
        {
            _semaphoreUpdatingList.Wait();
            _ctsUpdatingList = new CancellationTokenSource();
            var ct = _ctsUpdatingList.Token;
            Monitor.Enter(_lockDownloadItemsList);
            int total = objects.Count();
            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (objects[i] == null) continue;
                    int progress = (int)((double)(i + 1) / total * 100);
                    this.Progress = progress;
                    this.Status = "Listing " + (i + 1) + " of " + objects.Count() + " (" + progress + "%): " + objects[i].Name;
                    RaisePropertyChanged(nameof(this.Status));
                    RaisePropertyChanged(nameof(this.Progress));
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DownloadItemsList.Add(objects[i]);
                    });
                    if (objects[i].IsQueued)
                    {
                        if (!this.QueueProcessor.Add(objects[i]))
                        {
                            objects[i].Dequeue();
                        }
                    }
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                _semaphoreUpdatingList.Release();
                _ctsUpdatingList = null;
                this.Progress = 0;
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Progress));
                RaisePropertyChanged(nameof(this.Status));
                RefreshCollection();
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
            MessageBox.Show("Operation in progress. Please wait.", "Busy", MessageBoxButton.OK, MessageBoxImage.Exclamation);
        }
        #endregion // Methods
    }
}