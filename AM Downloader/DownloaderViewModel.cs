using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
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
        private const int PROPERTY_CHANGE_SPEED = 100; // 1 property update per this time (ms)
        private readonly ICollectionView _collectionView;
        private ClipboardObserver _clipboardService;
        private object _lockDownloadItemsList;
        private object _lockDownloadingCount;
        private object _lockQueuedCount;
        private SemaphoreSlim _semaphorePropertyChanged;
        private SemaphoreSlim _semaphoreCollectionRefresh;

        public string Status { get; private set; }
        public string TotalSpeed { get; private set; }
        public int DownloadingCount { get; private set; }
        public int QueuedCount { get; private set; }

        public HttpClient Client;
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public ObservableCollection<Categories> CategoriesList;
        public QueueProcessor QueueProcessor;
        public event PropertyChangedEventHandler PropertyChanged;

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

        public enum Categories
        {
            All, Ready, Queued, Downloading, Paused, Finished, Error
        }

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
            _lockQueuedCount = QueuedCount;
            _lockDownloadingCount = DownloadingCount;
            _semaphorePropertyChanged = new SemaphoreSlim(1);
            _semaphoreCollectionRefresh = new SemaphoreSlim(1);
            QueuedCount = 0;
            DownloadingCount = 0;

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
                                    Item_DownloadStarted,
                                    Item_DownloadFinished,
                                    Item_Enqueued,
                                    Item_Dequeued,
                                    OnDownloadPropertyChange,
                                    RefreshCollection);

                                DownloadItemsList.Add(item);
                                item.SetCreationTime(sItem.DateCreated);
                                if (sItem.IsQueued)
                                {
                                    QueueProcessor.Add(item);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex.Message);
                            }
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

        void CategoryChanged(object obj)
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

        void Start(object obj)
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

        public bool Start_CanExecute(object obj)
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

        void Pause(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
                item.Pause();
        }

        public bool Pause_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null) return false;

            foreach (var item in items)
                if (item.Status == DownloadStatus.Downloading) return true;

            return false;
        }

        void Cancel(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
                item.Cancel();
        }

        public bool Cancel_CanExecute(object obj)
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

        void RemoveFromList(object obj)
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
                }
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        public bool RemoveFromList_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null || items.Count() == 0) return false;

            return true;
        }

        void Add(object obj)
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

        void Open(object obj)
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

        public bool Open_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsFinished = from item in items where item.Status == DownloadStatus.Finished where new FileInfo(item.Destination).Exists select item;

            if (itemsFinished.Count() > 0) return true;

            return false;
        }

        void OpenContainingFolder(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsExist = from item in items where new FileInfo(item.Destination).Exists select item;

            foreach (var item in itemsExist)
                Process.Start("explorer.exe", "/select, \"\"" + item.Destination + "\"\"");
        }

        bool OpenContainingFolder_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsExist = from item in items where new FileInfo(item.Destination).Exists select item;

            if (itemsExist.Count() > 0) return true;

            return false;
        }

        void StartQueue(object obj)
        {
            Task.Run(async () => await QueueProcessor.StartAsync(Settings.Default.MaxConnectionsPerDownload));
        }

        public bool StartQueue_CanExecute(object obj)
        {
            return (!QueueProcessor.IsBusy && QueueProcessor.Count() > 0);
        }

        void StopQueue(object obj)
        {
            QueueProcessor.Stop();
        }

        public bool StopQueue_CanExecute(object obj)
        {
            return (obj != null || QueueProcessor.IsBusy);
        }

        void WindowClosing(object obj)
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

        void ShowOptions(object obj)
        {
            var win = new OptionsWindow();
            win.Owner = obj as Window;
            win.ShowDialog();
        }

        void Enqueue(object obj)
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

        bool Enqueue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            return (from item in items where item.IsQueued == false where item.Status == DownloadStatus.Ready select item).Count<DownloaderObjectModel>() > 0;
        }

        void Dequeue(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
                item.Dequeue();
        }

        bool Dequeue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
                if (item.IsQueued && !item.IsBeingDownloaded) return true;

            return false;
        }

        void DeleteFile(object obj)
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                        continue;
                    }
                    finally
                    {
                        DownloadItemsList.Remove(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        bool DeleteFile_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsDeletable = from item in items where !item.IsBeingDownloaded where File.Exists(item.Destination) select item;

            if (itemsDeletable.Count() > 0) return true;
            return false;
        }

        void CopyLinkToClipboard(object obj)
        {
            if (obj == null) return;
            var item = obj as DownloaderObjectModel;
            _clipboardService.SetText(item.Url);
        }

        bool CopyLinkToClipboard_CanExecute(object obj)
        {
            if (obj == null) return false;
            return true;
        }

        void ClearFinishedDownloads(object obj)
        {
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
                RefreshCollection();
            }
        }

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public void OnDownloadPropertyChange(object sender, PropertyChangedEventArgs e)
        {
            /* Task.Run(async () =>
               {
                   await _semaphorePropertyChanged.WaitAsync();

                   Stopwatch sw = new Stopwatch();
                   sw.Start();

                   string totalspeed = string.Empty;
                   long t_speed = 0;
                   IEnumerable<long> T_speed = null;
                   int countDownloading = 0;
                   int countQueued = 0;
                   int countFinished = 0;
                   string status = "Ready";

                   //Monitor.Enter(_lock);

                   try
                   {
                       //copy = DownloadItemsList.ToList<DownloaderObjectModel>();
                   }
                   catch
                   {
                       _semaphorePropertyChanged.Release();
                       return;
                   }
                   finally
                   {
                       //Monitor.Exit(_lock);
                   }

                   T_speed = from item in DownloadItemsList where item.IsBeingDownloaded select item.Speed ?? 0;
                   countDownloading = (from item in DownloadItemsList where item.IsBeingDownloaded select item).Count();
                   countQueued = (from item in DownloadItemsList where item.IsQueued select item).Count();
                   countFinished = (from item in DownloadItemsList where item.IsCompleted select item).Count();

                   if (T_speed?.Count() > 0)
                   {
                       foreach (long speed in T_speed) t_speed += speed;
                       if (t_speed > 0) totalspeed = PrettifySpeed(t_speed);
                   }

                   if (countDownloading > 0) status = countDownloading + " item(s) downloading";
                   if (countQueued > 0) status += "\t" + countQueued + " item(s) queued";
                   if (countFinished > 0) status += "\t" + countFinished + " item(s) finished";

                   if (this.Status != status)
                   {
                       this.Status = status;
                       AnnouncePropertyChanged(nameof(this.Status));
                   }

                   if (this.TotalSpeed != totalspeed)
                   {
                       this.TotalSpeed = totalspeed;
                       AnnouncePropertyChanged(nameof(this.TotalSpeed));
                   }

                   sw.Stop();
                   //if (sw.ElapsedMilliseconds < PROPERTY_CHANGE_SPEED) await Task.Delay(PROPERTY_CHANGE_SPEED - (int)sw.ElapsedMilliseconds);

                   _semaphorePropertyChanged.Release();
               }); */
        }

        internal void RefreshCollection()
        {
            Task.Run(async () =>
            {
                await _semaphoreCollectionRefresh.WaitAsync();
                Stopwatch sw = new Stopwatch();
                sw.Start();

                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        _collectionView?.Refresh();
                    });
                }
                catch { }
                finally
                {
                    sw.Stop();
                    if (sw.ElapsedMilliseconds < 1000) await Task.Delay(1000 - (int)sw.ElapsedMilliseconds);
                    _semaphoreCollectionRefresh.Release();
                }
            });
        }

        public void Item_DownloadStarted(object sender, EventArgs e)
        {
            Monitor.Enter(_lockDownloadingCount);
            try
            {
                this.DownloadingCount++;
                AnnouncePropertyChanged(nameof(this.DownloadingCount));
            }
            finally
            {
                Monitor.Exit(_lockDownloadingCount);
            }
        }

        public void Item_DownloadFinished(object sender, EventArgs e)
        {
            Monitor.Enter(_lockDownloadingCount);
            try
            {
                this.DownloadingCount--;
                AnnouncePropertyChanged(nameof(this.DownloadingCount));
            }
            finally
            {
                Monitor.Exit(_lockDownloadingCount);
            }
        }

        public void Item_Enqueued(object sender, EventArgs e)
        {
            Monitor.Enter(_lockQueuedCount);
            try
            {
                this.QueuedCount++;
                AnnouncePropertyChanged(nameof(this.QueuedCount));
            }
            finally
            {
                Monitor.Exit(_lockQueuedCount);
            }
        }

        public void Item_Dequeued(object sender, EventArgs e)
        {
            Monitor.Enter(_lockQueuedCount);
            try
            {
                this.QueuedCount--;
                AnnouncePropertyChanged(nameof(this.QueuedCount));
            }
            finally
            {
                Monitor.Exit(_lockQueuedCount);
            }
        }
    }
}
