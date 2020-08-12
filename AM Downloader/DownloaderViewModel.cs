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
        private object _lockDownloadingCount;
        private object _lockQueuedCount;
        private object _lockFinishedCount;
        private object _lockDownloadingItemsList;
        private SemaphoreSlim _semaphoreCollectionRefresh;
        private SemaphoreSlim _semaphoreMeasuringSpeed;
        private SemaphoreSlim _semaphoreUpdatingList;
        private List<DownloaderObjectModel> _downloadingItems;
        #endregion // Fields

        #region Properties
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public long? Speed { get; private set; }
        public int DownloadingCount { get; private set; }
        public int QueuedCount { get; private set; }
        public int FinishedCount { get; private set; }
        public HttpClient Client;
        public ObservableCollection<Categories> CategoriesList;
        public QueueProcessor QueueProcessor;
        public event PropertyChangedEventHandler PropertyChanged;
        public enum Categories
        {
            All, Ready, Queued, Downloading, Paused, Finished, Error
        }
        public AddItemsAsync AddItemsAsyncDelegate;
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
        public ICommand WindowClosingCommand { get; private set; }
        public ICommand CategoryChangedCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand EnqueueCommand { get; private set; }
        public ICommand DequeueCommand { get; private set; }
        public ICommand DeleteFileCommand { get; private set; }
        public ICommand CopyLinkToClipboardCommand { get; private set; }
        public ICommand ClearFinishedDownloadsCommand { get; private set; }
        #endregion // Commands

        #region Constructors
        public DownloaderViewModel()
        {
            Client = new HttpClient();
            Client.Timeout = new TimeSpan(0, 0, 0, 15, 0);
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            CategoriesList = new ObservableCollection<Categories>();
            QueueProcessor = new QueueProcessor(Settings.Default.MaxParallelDownloads, RefreshCollection);
            _downloadingItems = new List<DownloaderObjectModel>();
            _collectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            _clipboardService = new ClipboardObserver();
            _lockDownloadItemsList = DownloadItemsList;
            _lockQueuedCount = this.QueuedCount;
            _lockDownloadingCount = this.DownloadingCount;
            _lockFinishedCount = this.FinishedCount;
            _lockDownloadingItemsList = _downloadingItems;
            _semaphoreCollectionRefresh = new SemaphoreSlim(1);
            _semaphoreMeasuringSpeed = new SemaphoreSlim(1);
            _semaphoreUpdatingList = new SemaphoreSlim(1);
            this.QueuedCount = 0;
            this.DownloadingCount = 0;
            this.FinishedCount = 0;
            this.AddItemsAsyncDelegate = new AddItemsAsync(AddItemsAsync);

            AddCommand = new RelayCommand(Add);
            StartCommand = new RelayCommand(Start, Start_CanExecute);
            RemoveFromListCommand = new RelayCommand(RemoveFromList, RemoveFromList_CanExecute);
            CancelCommand = new RelayCommand(Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand(Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand(Open, Open_CanExecute);
            OpenContainingFolderCommand = new RelayCommand(OpenContainingFolder, OpenContainingFolder_CanExecute);
            StartQueueCommand = new RelayCommand(StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand(StopQueue, StopQueue_CanExecute);
            WindowClosingCommand = new RelayCommand(WindowClosing);
            CategoryChangedCommand = new RelayCommand(CategoryChanged);
            OptionsCommand = new RelayCommand(ShowOptions);
            EnqueueCommand = new RelayCommand(Enqueue, Enqueue_CanExecute);
            DequeueCommand = new RelayCommand(Dequeue, Dequeue_CanExecute);
            DeleteFileCommand = new RelayCommand(DeleteFile, DeleteFile_CanExecute);
            CopyLinkToClipboardCommand = new RelayCommand(CopyLinkToClipboard, CopyLinkToClipboard_CanExecute);
            ClearFinishedDownloadsCommand = new RelayCommand(ClearFinishedDownloads);

            foreach (Categories cat in (Categories[])Enum.GetValues(typeof(Categories)))
                CategoriesList.Add(cat);

            // Populate history
            if (Directory.Exists(ApplicationPaths.DownloadsHistory))
            {
                Task.Run(() =>
                {
                    SerializableDownloaderObjectModelList list;
                    var items = new List<DownloaderObjectModel>();
                    var xmlReader = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    try
                    {
                        using (var streamReader = new StreamReader(Path.Combine(ApplicationPaths.DownloadsHistory, "history.xml")))
                        {
                            list = (SerializableDownloaderObjectModelList)xmlReader.Deserialize(streamReader);
                        }
                        foreach (var obj in list.Objects)
                        {
                            if (obj == null) continue;
                            var item = new DownloaderObjectModel(ref Client, obj.Url, obj.Destination, obj.IsQueued, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, RefreshCollection);
                            item.SetCreationTime(obj.DateCreated);
                            items.Add(item);
                        }
                        AddObjects(items.ToArray());
                    }
                    catch
                    {
                        return;
                    }
                });
            }
        }
        #endregion // Constructors

        #region Methods
        internal void CategoryChanged(object obj)
        {
            if (obj == null) return;

            var selectedCategory = (Categories)obj;

            switch (selectedCategory)
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
                case Categories.Error:
                    _collectionView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        if (item.Status == DownloadStatus.Error) return true;
                        return false;
                    });
                    break;
            }
        }

        internal void Start(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            int counter = 0;
            var tasks = new List<Task>();

            foreach (DownloaderObjectModel item in items)
            {
                if (item.IsBeingDownloaded) continue;
                if (item.IsQueued) item.Dequeue();
                tasks.Add(item.StartAsync());
                if (++counter > Settings.Default.MaxParallelDownloads) break;
            }

            Task.WhenAll(tasks);
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
                item.Pause();
        }

        internal bool Pause_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            if (items.Count() == 0) return false;

            foreach (var item in items)
                if (item.Status == DownloadStatus.Downloading) return true;

            return false;
        }

        internal void Cancel(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
                item.Cancel();
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
            if (_semaphoreUpdatingList.CurrentCount == 0) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            _semaphoreUpdatingList.Wait();
            Monitor.Enter(_lockDownloadItemsList);
            Monitor.Enter(_lockFinishedCount);
            try
            {
                foreach (DownloaderObjectModel item in items)
                {
                    DownloadItemsList.Remove(item);
                    if (item.Status == DownloadStatus.Finished)
                    {
                        this.FinishedCount--;
                    }
                    if (item.IsBeingDownloaded) item.Cancel();
                    if (item.IsQueued) item.Dequeue();
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                Monitor.Exit(_lockFinishedCount);
                _semaphoreUpdatingList.Release();
                RaisePropertyChanged(nameof(this.FinishedCount));
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
            if (_semaphoreUpdatingList.CurrentCount == 0) return;
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
                Process.Start("explorer.exe", "\"" + item.Destination + "\"");
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
                Process.Start("explorer.exe", "/select, \"\"" + item.Destination + "\"\"");
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
            Task.Run(async () => await QueueProcessor.StartAsync(Settings.Default.MaxConnectionsPerDownload));
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

        internal void WindowClosing(object obj)
        {
            try
            {
                if (Directory.Exists(ApplicationPaths.DownloadsHistory)) Directory.Delete(ApplicationPaths.DownloadsHistory, true);
                Directory.CreateDirectory(ApplicationPaths.DownloadsHistory);
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
                    sItem.DateCreated = item.DateCreated;
                    list.Objects.Add(sItem);
                }
                using (var streamWriter = new StreamWriter(Path.Combine(ApplicationPaths.DownloadsHistory, "history.xml"), false))
                {
                    writer.Serialize(streamWriter, list);
                }
            }
            finally
            {
                Application.Current.Shutdown();
            }
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
                if (!item.IsQueued && item.Status == DownloadStatus.Ready)
                {
                    item.Enqueue();
                    QueueProcessor.Add(item);
                }
            }
        }

        internal bool Enqueue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            return (from item in items where item.IsQueued == false where item.Status == DownloadStatus.Ready select item).Count<DownloaderObjectModel>() > 0;
        }

        internal void Dequeue(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
                item.Dequeue();
        }

        internal bool Dequeue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            foreach (var item in items)
                if (item.IsQueued && !item.IsBeingDownloaded) return true;

            return false;
        }

        internal void DeleteFile(object obj)
        {
            if (obj == null) return;
            if (_semaphoreUpdatingList.CurrentCount == 0) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsDeletable = from item in items where !item.IsBeingDownloaded where File.Exists(item.Destination) select item;
            var itemsDeleted = new BlockingCollection<DownloaderObjectModel>();

            Task.Run(async () =>
            {
                await _semaphoreUpdatingList.WaitAsync();
                foreach (var item in itemsDeletable)
                {
                    try
                    {
                        FileSystem.DeleteFile(item.Destination, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        itemsDeleted.TryAdd(item);
                    }
                    catch
                    {
                        continue;
                    }
                }
                Monitor.Enter(_lockFinishedCount);
                Monitor.Enter(_lockDownloadItemsList);
                try
                {
                    foreach (var item in itemsDeleted)
                    {
                        if (item.IsQueued) item.Dequeue();
                        if (item.Status == DownloadStatus.Finished) this.FinishedCount--;
                        Application.Current.Dispatcher.Invoke(() => DownloadItemsList.Remove(item));
                    }
                }
                finally
                {
                    Monitor.Exit(_lockFinishedCount);
                    Monitor.Exit(_lockDownloadItemsList);
                    _semaphoreUpdatingList.Release();
                    RaisePropertyChanged(nameof(this.FinishedCount));
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

        internal void CopyLinkToClipboard(object obj)
        {
            if (obj == null) return;
            var item = obj as DownloaderObjectModel;
            _clipboardService.SetText(item.Url);
        }

        internal bool CopyLinkToClipboard_CanExecute(object obj)
        {
            if (obj == null) return false;
            return true;
        }

        internal void ClearFinishedDownloads(object obj)
        {
            _semaphoreUpdatingList.Wait();
            Monitor.Enter(_lockDownloadItemsList);
            try
            {
                var itemsFinished = (from item in DownloadItemsList where item.Status == DownloadStatus.Finished select item).ToArray<DownloaderObjectModel>();

                foreach (var item in itemsFinished)
                {
                    DownloadItemsList.Remove(item);

                    Monitor.Enter(_lockFinishedCount);
                    try
                    {
                        this.FinishedCount--;
                    }
                    finally
                    {
                        Monitor.Exit(_lockFinishedCount);
                    }
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

        internal void RefreshCollection()
        {
            if (_semaphoreCollectionRefresh.CurrentCount == 0) return;
            Task.Run(async () =>
            {
                await _semaphoreCollectionRefresh.WaitAsync();
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                Application.Current?.Dispatcher?.Invoke(_collectionView.Refresh);
                stopWatch.Stop();
                if (stopWatch.ElapsedMilliseconds < COLLECTION_REFRESH_INTERVAL) await Task.Delay(COLLECTION_REFRESH_INTERVAL - (int)stopWatch.ElapsedMilliseconds);
                _semaphoreCollectionRefresh.Release();
            });
        }

        internal void Download_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {

        }

        internal void Download_Started(object sender, EventArgs e)
        {
            Monitor.Enter(_lockDownloadingCount);
            Monitor.Enter(_lockDownloadingItemsList);
            try
            {
                this.DownloadingCount++;
                _downloadingItems.Add(sender as DownloaderObjectModel);
            }
            finally
            {
                Monitor.Exit(_lockDownloadingCount);
                Monitor.Exit(_lockDownloadingItemsList);
                RaisePropertyChanged(nameof(this.DownloadingCount));
            }
            StartMeasuringSpeed();
        }

        internal void Download_Stopped(object sender, EventArgs e)
        {
            Monitor.Enter(_lockDownloadingCount);
            Monitor.Enter(_lockDownloadingItemsList);
            try
            {
                this.DownloadingCount--;
                var item = sender as DownloaderObjectModel;
                if (_downloadingItems.Contains(item))
                {
                    _downloadingItems.Remove(item);
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadingCount);
                Monitor.Exit(_lockDownloadingItemsList);
                RaisePropertyChanged(nameof(this.DownloadingCount));
            }
        }

        internal void Download_Enqueued(object sender, EventArgs e)
        {
            Monitor.Enter(_lockQueuedCount);
            try
            {
                this.QueuedCount++;
            }
            finally
            {
                Monitor.Exit(_lockQueuedCount);
                RaisePropertyChanged(nameof(this.QueuedCount));
            }
        }

        internal void Download_Dequeued(object sender, EventArgs e)
        {
            Monitor.Enter(_lockQueuedCount);
            try
            {
                this.QueuedCount--;
            }
            finally
            {
                Monitor.Exit(_lockQueuedCount);
                RaisePropertyChanged(nameof(this.QueuedCount));
            }
        }

        internal void Download_Finished(object sender, EventArgs e)
        {
            Monitor.Enter(_lockFinishedCount);
            try
            {
                this.FinishedCount++;
            }
            finally
            {
                Monitor.Exit(_lockFinishedCount);
                RaisePropertyChanged(nameof(this.FinishedCount));
            }
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private async Task AddItemsAsync(string destination, bool enqueue, bool start = false, params string[] urls)
        {
            int counter = 0;
            int maxParallelDownloads = Settings.Default.MaxParallelDownloads;
            int maxConnectionsPerDownload = Settings.Default.MaxConnectionsPerDownload;
            var items = new DownloaderObjectModel[urls.Count()];
            var tasks = new List<Task>();
            foreach (var url in urls)
            {
                var fileName = GetValidFilename(destination + Path.GetFileName(url));
                var checkifUrlExists = from di in DownloadItemsList where di.Url == url select di;
                var checkIfDestinationExists = from di in DownloadItemsList where di.Destination == fileName select di;
                var sameItems = checkIfDestinationExists.Intersect(checkifUrlExists);

                if (sameItems.Count() > 0) return;

                var item = new DownloaderObjectModel(ref Client, url, fileName, enqueue, Download_Started, Download_Stopped, Download_Enqueued, Download_Dequeued, Download_Finished, Download_PropertyChanged, RefreshCollection);
                items[counter] = item;

                // Do not start more than MaxParallelDownloads at the same time
                if (!enqueue && start)
                {
                    if (counter < maxParallelDownloads)
                    {
                        tasks.Add(item.StartAsync());
                    }
                }
                counter++;
            }
            AddObjects(items);
            if (enqueue && start)
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
            Monitor.Enter(_lockDownloadItemsList);
            try
            {
                foreach (var item in objects)
                {
                    if (item == null) continue;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DownloadItemsList.Add(item);
                    });

                    if (item.IsQueued)
                    {
                        if (!this.QueueProcessor.Add(item)) item.Dequeue();
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                _semaphoreUpdatingList.Release();
                RefreshCollection();
            }
        }

        private void StartMeasuringSpeed()
        {
            if (_semaphoreMeasuringSpeed.CurrentCount == 0) return;

            Task.Run(async () =>
            {
                await _semaphoreMeasuringSpeed.WaitAsync();
                var stopWatch = new Stopwatch();
                long bytesFrom;
                long bytesTo;
                long bytesCaptured;
                while (_downloadingItems.Count > 0)
                {
                    bytesFrom = 0;
                    bytesTo = 0;
                    stopWatch.Start();
                    foreach (var item in _downloadingItems)
                    {
                        bytesFrom += item.TotalBytesCompleted;
                    }
                    await Task.Delay(1000);
                    foreach (var item in _downloadingItems)
                    {
                        bytesTo += item.TotalBytesCompleted;
                    }
                    stopWatch.Stop();
                    bytesCaptured = bytesTo - bytesFrom;
                    if (bytesCaptured >= 0 && stopWatch.ElapsedMilliseconds > 0)
                    {
                        this.Speed = (long)((double)bytesCaptured / ((double)stopWatch.ElapsedMilliseconds / 1000));
                        RaisePropertyChanged(nameof(this.Speed));
                    }
                    stopWatch.Reset();
                }
                this.Speed = null;
                RaisePropertyChanged(nameof(this.Speed));
                _semaphoreMeasuringSpeed.Release();
            });
        }
        #endregion // Methods
    }
}