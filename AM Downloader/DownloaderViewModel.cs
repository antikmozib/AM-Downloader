using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.ObjectModel;
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
        private SemaphoreSlim _semaphoreCollectionRefresh;
        #endregion // Fields

        #region Properties
        public string Status { get; private set; }
        public string TotalSpeed { get; private set; }
        public int DownloadingCount { get; private set; }
        public int QueuedCount { get; private set; }
        public int FinishedCount { get; private set; }
        public HttpClient Client;
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public ObservableCollection<Categories> CategoriesList;
        public QueueProcessor QueueProcessor;
        public event PropertyChangedEventHandler PropertyChanged;
        public enum Categories
        {
            All, Ready, Queued, Downloading, Paused, Finished, Error
        }
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
            QueueProcessor = new QueueProcessor(Settings.Default.MaxParallelDownloads);
            _collectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            _clipboardService = new ClipboardObserver();
            _lockDownloadItemsList = DownloadItemsList;
            _lockQueuedCount = this.QueuedCount;
            _lockDownloadingCount = this.DownloadingCount;
            _lockFinishedCount = this.FinishedCount;
            _semaphoreCollectionRefresh = new SemaphoreSlim(1);
            this.QueuedCount = 0;
            this.DownloadingCount = 0;
            this.FinishedCount = 0;

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

            this.Status = "Ready";
            AnnouncePropertyChanged(nameof(this.Status));

            foreach (Categories cat in (Categories[])Enum.GetValues(typeof(Categories)))
                CategoriesList.Add(cat);

            // Populate history
            if (Directory.Exists(ApplicationPaths.DownloadsHistory))
            {
                Monitor.Enter(_lockDownloadItemsList);
                try
                {
                    var xmlReader = new XmlSerializer(typeof(SerializableDownloaderObjectModel));
                    foreach (var file in Directory.GetFiles(ApplicationPaths.DownloadsHistory))
                    {
                        using (var streamReader = new StreamReader(file))
                        {
                            SerializableDownloaderObjectModel sItem;

                            try
                            {
                                sItem = (SerializableDownloaderObjectModel)xmlReader.Deserialize(streamReader);
                                var item = new DownloaderObjectModel(
                                    ref Client,
                                    sItem.Url,
                                    sItem.Destination,
                                    sItem.IsQueued,
                                    Download_Started,
                                    Download_Stopped,
                                    Download_Enqueued,
                                    Download_Dequeued,
                                    Download_Finished,
                                    Download_PropertyChanged,
                                    RefreshCollection);

                                DownloadItemsList.Add(item);
                                item.SetCreationTime(sItem.DateCreated);
                                if (sItem.IsQueued)
                                {
                                    QueueProcessor.Add(item);
                                }
                            }
                            catch { }
                        }
                    }
                }
                finally
                {
                    Monitor.Exit(_lockDownloadItemsList);
                    RefreshCollection();
                }
            }
        }
        #endregion // Constructors

        #region Private methods
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

            foreach (DownloaderObjectModel item in items)
            {
                if (item.IsBeingDownloaded) continue;
                if (item.IsQueued) item.Dequeue();
                Task.Run(() => item.StartAsync(Properties.Settings.Default.MaxConnectionsPerDownload));
                if (++counter > Settings.Default.MaxParallelDownloads) break;
            }
        }

        internal bool Start_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null) return false;

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

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null) return false;

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

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            if (items == null) return false;

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

            Monitor.Enter(_lockDownloadItemsList);

            try
            {
                var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

                foreach (DownloaderObjectModel item in items)
                {
                    if (item.IsBeingDownloaded) item.Cancel();
                    if (item.IsQueued) item.Dequeue();
                    DownloadItemsList.Remove(item);
                    if (item.Status == DownloadStatus.Finished)
                    {
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
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                AnnouncePropertyChanged(nameof(this.FinishedCount));
            }
        }

        internal bool RemoveFromList_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null || items.Count() == 0) return false;

            return true;
        }

        internal void Add(object obj)
        {
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
                    "Open", MessageBoxButton.YesNo, MessageBoxImage.Information);

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
            Monitor.Enter(_lockDownloadItemsList);

            try
            {
                XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloaderObjectModel));
                int i = 0;

                if (!Directory.Exists(ApplicationPaths.DownloadsHistory))
                {
                    Directory.CreateDirectory(ApplicationPaths.DownloadsHistory);
                }

                // Clear existing history
                foreach (var file in Directory.GetFiles(ApplicationPaths.DownloadsHistory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        continue;
                    }
                }

                foreach (var item in DownloadItemsList)
                {
                    if (item.IsBeingDownloaded)
                    {
                        item.Pause();
                    }

                    if (item.Status == DownloadStatus.Finished && Settings.Default.ClearFinishedOnExit)
                    {
                        continue;
                    }

                    StreamWriter streamWriter = new StreamWriter(Path.Combine(ApplicationPaths.DownloadsHistory, (++i).ToString() + ".xml"));

                    var sItem = new SerializableDownloaderObjectModel();

                    sItem.Url = item.Url;
                    sItem.Destination = item.Destination;
                    sItem.IsQueued = item.IsQueued;
                    sItem.DateCreated = item.DateCreated;

                    writer.Serialize(streamWriter, sItem);
                    streamWriter.Close();
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
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
                    if (!QueueProcessor.Contains(item))
                    {
                        QueueProcessor.Add(item);
                    }
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

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
                if (item.IsQueued && !item.IsBeingDownloaded) return true;

            return false;
        }

        internal void DeleteFile(object obj)
        {
            if (obj == null) return;

            Monitor.Enter(_lockDownloadItemsList);

            try
            {
                var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
                var itemsDeletable = from item in items where !item.IsBeingDownloaded where File.Exists(item.Destination) select item;

                foreach (var item in itemsDeletable)
                {
                    try
                    {
                        FileSystem.DeleteFile(item.Destination, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        if (item.Status == DownloadStatus.Finished)
                        {
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
                    catch
                    {
                        continue;
                    }
                    finally
                    {
                        DownloadItemsList.Remove(item);
                    }
                }

            }
            catch { }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
                AnnouncePropertyChanged(nameof(this.FinishedCount));
            }
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
                RefreshCollection();
                AnnouncePropertyChanged(nameof(this.FinishedCount));
            }
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        internal void RefreshCollection()
        {
            Task.Run(async () =>
            {
                if (_semaphoreCollectionRefresh.CurrentCount == 0) return;

                await _semaphoreCollectionRefresh.WaitAsync();

                Stopwatch sw = new Stopwatch(); 
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                     sw.Start();
                     _collectionView?.Refresh();
                     sw.Stop();
                });
                if (sw.ElapsedMilliseconds < COLLECTION_REFRESH_INTERVAL)
                    await Task.Delay(COLLECTION_REFRESH_INTERVAL - (int)sw.ElapsedMilliseconds);

                _semaphoreCollectionRefresh.Release();
            });
        }

        internal void Download_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {

        }

        internal void Download_Started(object sender, EventArgs e)
        {
            Monitor.Enter(_lockDownloadingCount);
            try
            {
                this.DownloadingCount++;
            }
            finally
            {
                Monitor.Exit(_lockDownloadingCount);
            }
            AnnouncePropertyChanged(nameof(this.DownloadingCount));
        }

        internal void Download_Stopped(object sender, EventArgs e)
        {
            Monitor.Enter(_lockDownloadingCount);
            try
            {
                this.DownloadingCount--;
            }
            finally
            {
                Monitor.Exit(_lockDownloadingCount);
            }
            AnnouncePropertyChanged(nameof(this.DownloadingCount));
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
            }
            AnnouncePropertyChanged(nameof(this.QueuedCount));
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
            }
            AnnouncePropertyChanged(nameof(this.QueuedCount));
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
            }
            AnnouncePropertyChanged(nameof(this.FinishedCount));
        }
        #endregion // Private methods
    }
}
