using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
using System.Linq;
using System.ComponentModel;
using System;
using System.Windows.Data;
using System.IO;
using System.Xml.Serialization;
using AMDownloader.Properties;
using static AMDownloader.SerializableModels;
using static AMDownloader.DownloaderObjectModel;
using static AMDownloader.Common;

namespace AMDownloader
{
    class DownloaderViewModel : INotifyPropertyChanged
    {
        private readonly ICollectionView _collectionView;

        public string StatusDownloading { get; private set; }
        public string StatusSpeed { get; private set; }
        public string StatusQueued { get; private set; }

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

        public enum Categories
        {
            All, Ready, Queued, Downloading, Paused, Finished, Error
        }

        public DownloaderViewModel()
        {
            Client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            CategoriesList = new ObservableCollection<Categories>();
            QueueProcessor = new QueueProcessor(Settings.Default.MaxParallelDownloads);
            _collectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);

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

            this.StatusDownloading = "Ready";
            AnnouncePropertyChanged(nameof(this.StatusDownloading));

            foreach (Categories cat in (Categories[])Enum.GetValues(typeof(Categories)))
                CategoriesList.Add(cat);

            // Populate history

            if (Directory.Exists(PATH_TO_DOWNLOADS_HISTORY))
            {
                var xmlReader = new XmlSerializer(typeof(SerializableDownloaderObjectModel));
                foreach (var file in Directory.GetFiles(PATH_TO_DOWNLOADS_HISTORY))
                {
                    using (var streamReader = new StreamReader(file))
                    {
                        SerializableDownloaderObjectModel sItem;

                        try
                        {
                            sItem = (SerializableDownloaderObjectModel)xmlReader.Deserialize(streamReader);
                            var item = new DownloaderObjectModel(ref Client, sItem.Url, sItem.Destination, sItem.IsQueued, OnDownloadPropertyChange, RefreshCollection);

                            DownloadItemsList.Add(item);
                            item.SetCreationTime(sItem.DateCreated);
                            if (sItem.IsQueued)
                            {
                                QueueProcessor.Add(item);
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

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

            foreach (DownloaderObjectModel item in items)
            {
                if (item.IsBeingDownloaded) continue;
                if (item.IsQueued) item.Dequeue();
                Task.Run(() => item.StartAsync(Properties.Settings.Default.MaxConnectionsPerDownload));
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

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
            {
                if (item.IsBeingDownloaded || item.Status == DownloadStatus.Paused)
                {
                    MessageBoxResult result = MessageBox.Show("Cancel downloading \"" + item.Name + "\" ?",
                    "Cancel Download", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);

                    if (result == MessageBoxResult.No)
                        continue;
                    else
                        item.Cancel();
                }

                if (item.IsQueued) item.Dequeue();
                DownloadItemsList.Remove(item);
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
            var vm = new AddDownloadViewModel(this);
            var win = new AddDownloadWindow();
            win.DataContext = vm;
            win.Owner = obj as Window;
            win.ShowDialog();
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
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsInQueue = from item in items where item.IsQueued where !item.IsBeingDownloaded select item;

            if (!QueueProcessor.IsBusy && itemsInQueue.Count() > 0) return true;

            return false;
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
            XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloaderObjectModel));

            if (!Directory.Exists(PATH_TO_DOWNLOADS_HISTORY))
            {
                Directory.CreateDirectory(PATH_TO_DOWNLOADS_HISTORY);
            }

            // Clear existing history
            foreach (var file in Directory.GetFiles(PATH_TO_DOWNLOADS_HISTORY))
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

                StreamWriter streamWriter = new StreamWriter(Path.Combine(PATH_TO_DOWNLOADS_HISTORY, item.Name + ".xml"));

                var sItem = new SerializableDownloaderObjectModel();

                sItem.Url = item.Url;
                sItem.Destination = item.Destination;
                sItem.IsQueued = item.IsQueued;
                sItem.DateCreated = item.DateCreated;

                writer.Serialize(streamWriter, sItem);
                streamWriter.Close();
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

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsDeletable = from item in items where !item.IsBeingDownloaded where File.Exists(item.Destination) select item;

            foreach (var item in itemsDeletable)
            {
                if (item.IsBeingDownloaded) continue; // prevent race condition

                try
                {
                    File.Delete(item.Destination);
                    DownloadItemsList.Remove(item);
                }
                catch (Exception e)
                {
                    MessageBoxResult r = MessageBox.Show(e.Message, "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Error);
                    if (r == MessageBoxResult.Cancel) break;
                    continue;
                }
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

        protected void AnnouncePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public void OnDownloadPropertyChange(object sender, PropertyChangedEventArgs e)
        {
            string statusSpeed;
            string statusDownloading;
            string statusQueued;

            int countDownloading = 0;
            var downloadingItems = from item in DownloadItemsList where item.IsBeingDownloaded select item;
            var itemsinQueue = from item in DownloadItemsList where item.IsQueued where !item.IsBeingDownloaded select item;

            long totalspeed = 0;

            if (downloadingItems != null)
            {
                countDownloading = downloadingItems.Count();
                foreach (var item in downloadingItems)
                    totalspeed += item.Speed ?? 0;
            }

            if (totalspeed > 0)
            {
                statusSpeed = Common.PrettySpeed(totalspeed);
            }
            else
            {
                statusSpeed = string.Empty;
            }

            if (this.StatusSpeed != statusSpeed)
            {
                this.StatusSpeed = statusSpeed;
                AnnouncePropertyChanged(nameof(this.StatusSpeed));
            }


            if (countDownloading > 0)
            {
                statusDownloading = countDownloading + " item(s) downloading";
            }
            else
            {
                statusDownloading = "Ready";
            }

            if (itemsinQueue != null && itemsinQueue.Count() > 0)
            {
                statusQueued = itemsinQueue.Count() + " item(s) queued";
            }
            else
            {
                statusQueued = string.Empty;
            }

            if (this.StatusQueued != statusQueued)
            {
                this.StatusQueued = statusQueued;
                AnnouncePropertyChanged(nameof(this.StatusQueued));
            }

            if (this.StatusDownloading != statusDownloading)
            {
                this.StatusDownloading = statusDownloading;
                AnnouncePropertyChanged(nameof(this.StatusDownloading));
            }
        }

        internal void RefreshCollection()
        {
            Application.Current.Dispatcher.Invoke(() => _collectionView.Refresh());
        }
    }
}
