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
using static AMDownloader.DownloaderObjectModel;
using System.IO;

namespace AMDownloader
{
    class DownloaderViewModel : INotifyPropertyChanged
    {
        private ICollectionView _collectionView;

        public string StatusDownloading { get; private set; }
        public string StatusSpeed { get; private set; }
        public string StatusQueued { get; private set; }

        public HttpClient Client;
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public ObservableCollection<ViewStatus> CategoriesList;
        public QueueProcessor QueueProcessor;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand AddCommand { get; private set; }
        public ICommand StartCommand { get; private set; }
        public ICommand RemoveCommand { private get; set; }
        public ICommand CancelCommand { private get; set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand OpenContainingFolderCommand { get; private set; }
        public ICommand StartQueueCommand { get; private set; }
        public ICommand StopQueueCommand { get; private set; }
        public ICommand WindowClosingCommand { get; private set; }
        public ICommand CategoryChangedCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand AddToQueueCommand { get; private set; }
        public ICommand RemoveFromQueueCommand { get; private set; }

        public enum ViewStatus
        {
            All, Ready, Queued, Downloading, Paused, Finished, Error
        }

        public DownloaderViewModel()
        {
            Client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            CategoriesList = new ObservableCollection<ViewStatus>();
            QueueProcessor = new QueueProcessor(DownloadItemsList);
            _collectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);

            AddCommand = new RelayCommand(Add);
            StartCommand = new RelayCommand(Start, Start_CanExecute);
            RemoveCommand = new RelayCommand(Remove, Remove_CanExecute);
            CancelCommand = new RelayCommand(Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand(Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand(Open, Open_CanExecute);
            OpenContainingFolderCommand = new RelayCommand(OpenContainingFolder, OpenContainingFolder_CanExecute);
            StartQueueCommand = new RelayCommand(StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand(StopQueue, StopQueue_CanExecute);
            WindowClosingCommand = new RelayCommand(WindowClosing);
            CategoryChangedCommand = new RelayCommand(CategoryChanged);
            OptionsCommand = new RelayCommand(ShowOptions);
            AddToQueueCommand = new RelayCommand(AddToQueue, AddToQueue_CanExecute);
            RemoveFromQueueCommand = new RelayCommand(RemoveFromQueue, RemoveFromQueue_CanExecute);

            this.StatusDownloading = "Ready";
            AnnouncePropertyChanged(nameof(this.StatusDownloading));

            foreach (ViewStatus status in (ViewStatus[])Enum.GetValues(typeof(ViewStatus)))
            {
                CategoriesList.Add(status);
            }
        }

        void CategoryChanged(object item)
        {
            if (item == null) return;

            var status = (ViewStatus)item;

            switch (status)
            {
                case ViewStatus.All:
                    _collectionView.Filter = new Predicate<object>(FilterAll);
                    break;
                case ViewStatus.Downloading:
                    _collectionView.Filter = new Predicate<object>(FilterDownloading);
                    break;
                case ViewStatus.Finished:
                    _collectionView.Filter = new Predicate<object>(FilterFinished);
                    break;
                case ViewStatus.Paused:
                    _collectionView.Filter = new Predicate<object>(FilterPaused);
                    break;
                case ViewStatus.Queued:
                    _collectionView.Filter = new Predicate<object>(FilterQueued);
                    break;
                case ViewStatus.Ready:
                    _collectionView.Filter = new Predicate<object>(FilterReady);
                    break;
                case ViewStatus.Error:
                    _collectionView.Filter = new Predicate<object>(FilterError);
                    break;
            }
        }

        #region  View filters

        private bool FilterAll(object obj)
        {
            return true;
        }

        private bool FilterDownloading(object obj)
        {
            var item = obj as DownloaderObjectModel;

            if (item.IsBeingDownloaded)
            {
                return true;
            }

            return false;
        }

        private bool FilterReady(object obj)
        {
            var item = obj as DownloaderObjectModel;

            if (item.Status == DownloadStatus.Ready)
            {
                return true;
            }

            return false;
        }

        private bool FilterQueued(object obj)
        {
            var item = obj as DownloaderObjectModel;

            if (item.IsQueued)
            {
                return true;
            }

            return false;
        }

        private bool FilterFinished(object obj)
        {
            var item = obj as DownloaderObjectModel;

            if (item.Status == DownloadStatus.Finished)
            {
                return true;
            }

            return false;
        }

        private bool FilterPaused(object obj)
        {
            var item = obj as DownloaderObjectModel;

            if (item.Status == DownloadStatus.Paused)
            {
                return true;
            }

            return false;
        }

        private bool FilterError(object obj)
        {
            var item = obj as DownloaderObjectModel;

            if (item.Status == DownloadStatus.Error)
            {
                return true;
            }

            return false;
        }

        #endregion

        void Start(object obj)
        {
            if (obj == null)
            {
                return;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
            {
                if (item.IsBeingDownloaded)
                {
                    continue;
                }

                if (item.IsQueued)
                {
                    item.Dequeue();
                }

                Task.Run(() => item.StartAsync());
            }
        }

        public bool Start_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null)
            {
                return false;
            }

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
            if (obj == null)
            {
                return;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
            {
                item.Pause();
            }
        }

        public bool Pause_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null)
            {
                return false;
            }

            foreach (var item in items)
            {
                switch (item.Status)
                {
                    case DownloadStatus.Downloading:
                        return true;
                }
            }

            return false;
        }

        void Cancel(object obj)
        {
            if (obj == null)
            {
                return;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
            {
                item.Cancel();
            }
        }

        public bool Cancel_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null)
            {
                return false;
            }

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

        void Remove(object obj)
        {
            if (obj == null)
            {
                return;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel item in items)
            {
                if (item.IsBeingDownloaded)
                {
                    MessageBoxResult result = MessageBox.Show("Cancel downloading \"" + item.Name + "\" ?",
                    "Cancel Download", System.Windows.MessageBoxButton.YesNo);

                    if (result == MessageBoxResult.No)
                    {
                        continue;
                    }
                    else
                    {
                        item.Cancel();
                    }
                }

                DownloadItemsList.Remove(item);
                if (item.IsQueued) item.Dequeue();
            }
        }

        public bool Remove_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (items == null || items.Count() == 0)
            {
                return false;
            }

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

            foreach (var item in itemsFinished)
            {
                Process.Start(item.Destination);
            }
        }

        public bool Open_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsFinished = from item in items where item.Status == DownloadStatus.Finished where new FileInfo(item.Destination).Exists select item;

            if (itemsFinished.Count<DownloaderObjectModel>() > 0)
            {
                return true;
            }

            return false;
        }

        void OpenContainingFolder(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsExist = from item in items where new FileInfo(item.Destination).Exists select item;

            foreach (var item in itemsExist)
            {
                Process.Start("explorer.exe","/select, \"\"" + item.Destination + "\"\"");
            }
        }

        bool OpenContainingFolder_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsExist = from item in items where new FileInfo(item.Destination).Exists select item;

            if (itemsExist.Count<DownloaderObjectModel>() > 0)
            {
                return true;
            }

            return false;
        }

        void StartQueue(object obj)
        {
            Task.Run(async () => await QueueProcessor.StartAsync());
        }

        public bool StartQueue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            var itemsInQueue = from item in items where item.IsQueued where !item.IsBeingDownloaded select item;

            if (!QueueProcessor.IsBusy && itemsInQueue.Count() > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
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
            var items = from item in DownloadItemsList
                        where item.IsBeingDownloaded
                        select item;

            Parallel.ForEach(items, (item) =>
            {
                item.Pause();
            });
        }

        void ShowOptions(object obj)
        {
            var win = new OptionsWindow();
            win.Owner = obj as Window;
            win.ShowDialog();
        }

        void AddToQueue(object obj)
        {
            if (obj == null) return;
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
            {
                if (!item.IsQueued && item.Status == DownloadStatus.Ready)
                {
                    item.Enqueue(QueueProcessor);
                }
            }
        }

        bool AddToQueue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            return ((from item in items where item.IsQueued == false where item.Status == DownloadStatus.Ready select item).Count<DownloaderObjectModel>() > 0);
        }

        void RemoveFromQueue(object obj)
        {
            if (obj == null) return;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
            {
                item.Dequeue();
            }
        }

        bool RemoveFromQueue_CanExecute(object obj)
        {
            if (obj == null) return false;

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (var item in items)
            {
                if (item.IsQueued && !item.IsBeingDownloaded)
                {
                    return true;
                }
            }

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
                {
                    totalspeed += item.Speed ?? 0;
                }
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
    }
}
