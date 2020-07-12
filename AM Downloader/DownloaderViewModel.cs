using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Concurrent;
using static AMDownloader.DownloaderObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System;

namespace AMDownloader
{
    class DownloaderViewModel
    {
        public HttpClient Client;
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public QueueProcessor QProcessor;

        public ICommand AddCommand { get; private set; }
        public ICommand StartCommand { get; private set; }
        public ICommand RemoveCommand { private get; set; }
        public ICommand CancelCommand { private get; set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand StartQueueCommand { get; private set; }
        public ICommand StopQueueCommand { get; private set; }
        public ICommand WindowClosingCommand { get; private set; }

        public DownloaderViewModel()
        {
            Client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            QProcessor = new QueueProcessor();

            AddCommand = new RelayCommand(Add);
            StartCommand = new RelayCommand(Start, Start_CanExecute);
            RemoveCommand = new RelayCommand(Remove, Remove_CanExecute);
            CancelCommand = new RelayCommand(Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand(Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand(Open, Open_CanExecute);
            StartQueueCommand = new RelayCommand(StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand(StopQueue, StopQueue_CanExecute);
            WindowClosingCommand = new RelayCommand(WindowClosing);
        }

        void Start(object item)
        {
            if (item == null)
            {
                return;
            }

            var selectedItems = (item as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToList();
            /*var itemsInQueue = from qItem in selectedItems where qItem.IsQueued select qItem;
            var itemsNotInQueue = selectedItems.Except<DownloaderObjectModel>(itemsInQueue);

            if (itemsInQueue.Count() == 1)
            {
                Task.Run(() => QProcessor.StartAsync(itemsInQueue.First()));
            }
            else if (itemsInQueue.Count() > 1)
            {
                Task.Run(() => QProcessor.StartAsync(itemsInQueue.ToArray<DownloaderObjectModel>()));
            }*/

            foreach (DownloaderObjectModel dItem in selectedItems)
            {
                if (dItem.Status == DownloadStatus.Downloading)
                {
                    continue;
                }

                if (dItem.IsQueued)
                {
                    dItem.DeQueue();
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
            bool cancelAll = false;

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
            addDownloadWindow.ShowDialog();
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
            Task.Run(async () => await QProcessor.StartAsync());
        }

        public bool StartQueue_CanExecute(object item)
        {
            if (!QProcessor.IsBusy && QProcessor.Count > 0)
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
            QProcessor.Stop(DownloadItemsList);
        }

        public bool StopQueue_CanExecute(object item)
        {
            return (item != null || QProcessor.IsBusy);
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
    }
}
