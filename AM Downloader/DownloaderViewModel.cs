using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using static AMDownloader.DownloaderObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Linq;
using System.ComponentModel;
using System;
using System.Windows.Data;

namespace AMDownloader
{
    class DownloaderViewModel : INotifyPropertyChanged
    {
        private ICollectionView _collectionView;

        public HttpClient Client;
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public ObservableCollection<ViewStatus> CategoriesList;
        public QueueProcessor MainQueueProcessor;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand AddCommand { get; private set; }
        public ICommand StartCommand { get; private set; }
        public ICommand RemoveCommand { private get; set; }
        public ICommand CancelCommand { private get; set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand StartQueueCommand { get; private set; }
        public ICommand StopQueueCommand { get; private set; }
        public ICommand WindowClosingCommand { get; private set; }
        public ICommand CategoryChangedCommand { get; private set; }

        public enum ViewStatus
        {
            All, Ready, Queued, Downloading, Paused, Finished, Error
        }

        public DownloaderViewModel()
        {
            Client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            CategoriesList = new ObservableCollection<ViewStatus>();
            MainQueueProcessor = new QueueProcessor();
            _collectionView = CollectionViewSource.GetDefaultView(DownloadItemsList);

            AddCommand = new RelayCommand(Add);
            StartCommand = new RelayCommand(Start, Start_CanExecute);
            RemoveCommand = new RelayCommand(Remove, Remove_CanExecute);
            CancelCommand = new RelayCommand(Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand(Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand(Open, Open_CanExecute);
            StartQueueCommand = new RelayCommand(StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand(StopQueue, StopQueue_CanExecute);
            WindowClosingCommand = new RelayCommand(WindowClosing);
            CategoryChangedCommand = new RelayCommand(CategoryChanged);

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

            if (item.Status == DownloadStatus.Downloading)
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

        void Start(object item)
        {
            if (item == null)
            {
                return;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel dItem in selectedItems)
            {
                if (dItem.Status == DownloadStatus.Downloading)
                {
                    continue;
                }

                if (dItem.IsQueued)
                {
                    dItem.Dequeue();
                }

                Task.Run(() => dItem.StartAsync());
            }
        }

        public bool Start_CanExecute(object item)
        {
            if (item == null)
            {
                return false;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (selectedItems == null)
            {
                return false;
            }

            foreach (var dItem in selectedItems)
            {
                switch (dItem.Status)
                {
                    case DownloadStatus.Paused:
                    case DownloadStatus.Ready:
                    case DownloadStatus.Queued:
                        return true;
                }
            }

            return false;
        }

        void Pause(object item)
        {
            if (item == null)
            {
                return;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel dItem in selectedItems)
            {
                dItem.Pause();
            }
        }
        
        public bool Pause_CanExecute(object item)
        {
            if (item == null)
            {
                return false;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (selectedItems == null)
            {
                return false;
            }

            foreach (var dItem in selectedItems)
            {
                switch (dItem.Status)
                {
                    case DownloadStatus.Downloading:
                        return true;
                }
            }

            return false;
        }

        void Cancel(object item)
        {
            if (item == null)
            {
                return;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel dItem in selectedItems)
            {
                dItem.Cancel();
            }
        }

        public bool Cancel_CanExecute(object item)
        {
            if (item == null)
            {
                return false;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (selectedItems == null)
            {
                return false;
            }

            foreach (var dItem in selectedItems)
            {
                switch (dItem.Status)
                {
                    case DownloadStatus.Downloading:
                    case DownloadStatus.Paused:
                        return true;
                }
            }

            return false;
        }

        void Remove(object item)
        {
            if (item == null)
            {
                return;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();

            foreach (DownloaderObjectModel dItem in selectedItems)
            {
                if (dItem.Status == DownloadStatus.Downloading)
                {
                    MessageBoxResult result = MessageBox.Show("Cancel downloading \"" + dItem.Name + "\" ?",
                    "Cancel Download", System.Windows.MessageBoxButton.YesNo);

                    if (result == MessageBoxResult.No)
                    {
                        continue;
                    }
                    else
                    {
                        dItem.Cancel();
                    }
                }

                DownloadItemsList.Remove(dItem);
                if (dItem.IsQueued) dItem.Dequeue();
            }
        }

        public bool Remove_CanExecute(object item)
        {
            if (item == null)
            {
                return false;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            if (selectedItems == null || selectedItems.Count() == 0)
            {
                return false;
            }

            return true;
        }

        void Add(object item)
        {
            AddDownloadViewModel addDownloadViewModel = new AddDownloadViewModel(this);
            AddDownloadWindow addDownloadWindow = new AddDownloadWindow();
            addDownloadWindow.DataContext = addDownloadViewModel;

            addDownloadViewModel.Urls = @"https://download-installer.cdn.mozilla.net/pub/firefox/releases/77.0.1/win64/en-US/Firefox%20Setup%2077.0.1.exe" +
                '\n' + @"https://download3.operacdn.com/pub/opera/desktop/69.0.3686.49/win/Opera_69.0.3686.49_Setup_x64.exe";
            addDownloadViewModel.SaveToFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            addDownloadViewModel.AddToQueue = true;
            addDownloadViewModel.StartDownload = false;

            addDownloadWindow.Owner = item as Window;
            addDownloadWindow.Show();
        }

        void Open(object item)
        {
            if (item == null) return;

            DownloaderObjectModel dItem = item as DownloaderObjectModel;

            if (dItem.Status == DownloadStatus.Finished)
            {
                Process.Start(dItem.Destination);
            }
        }

        public bool Open_CanExecute(object item)
        {
            if (item != null)
            {
                var dItem = item as DownloaderObjectModel;
                if (dItem.Status == DownloadStatus.Finished)
                {
                    return true;
                }
            }

            return false;
        }

        void StartQueue(object item)
        {
            Task.Run(async () => await MainQueueProcessor.StartAsync());
        }

        public bool StartQueue_CanExecute(object item)
        {
            if (!MainQueueProcessor.IsBusy && MainQueueProcessor.Count() > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        void StopQueue(object item)
        {
            MainQueueProcessor.Stop();
        }

        public bool StopQueue_CanExecute(object item)
        {
            return (item != null || MainQueueProcessor.IsBusy);
        }

        void WindowClosing(object item)
        {
            var items = from dItem in DownloadItemsList
                        where dItem.Status == DownloadStatus.Downloading
                        select dItem;

            Parallel.ForEach(items, (dItem) =>
            {
                dItem.Pause();
            });
        }

        public string StatusDownloading { get; private set; }

        public string StatusSpeed { get; private set; }

        public string StatusQueued { get; private set; }

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
            var downloadingItems = from item in DownloadItemsList where item.Status == DownloadStatus.Downloading select item;
            var itemsinQueue = from item in DownloadItemsList where item.IsQueued where item.Status != DownloadStatus.Downloading select item;

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
