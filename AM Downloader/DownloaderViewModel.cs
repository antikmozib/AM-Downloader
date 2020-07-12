using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Concurrent;
using static AMDownloader.DownloaderObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Linq;

namespace AMDownloader
{
    class DownloaderViewModel
    {
        private BlockingCollection<DownloaderObjectModel> _queueList;

        public HttpClient Client;
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public QueueProcessor QProcessor;

        public RelayCommand AddCommand { get; private set; }
        public ICommand StartCommand { get; private set; }
        public RelayCommand RemoveCommand { private get; set; }
        public RelayCommand CancelCommand { private get; set; }
        public RelayCommand PauseCommand { get; private set; }
        public RelayCommand OpenCommand { get; private set; }
        public RelayCommand StartQueueCommand { get; private set; }
        public RelayCommand StopQueueCommand { get; private set; }
        public RelayCommand WindowClosingCommand { get; private set; }

        public DownloaderViewModel()
        {
            Client = new HttpClient();
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            _queueList = new BlockingCollection<DownloaderObjectModel>();
            QProcessor = new QueueProcessor(ref this._queueList);

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
            if (item == null) return;

            var downloaderItem = item as DownloaderObjectModel;
            Task.Run(() => downloaderItem.StartAsync());
        }

        public bool Start_CanExecute(object item)
        {
            if (item == null) return false;

            var dItem = item as DownloaderObjectModel;
            if (dItem.Status == DownloadStatus.Downloading)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        void Pause(object item)
        {
            if (item == null) return;

            DownloaderObjectModel downloaderItem = item as DownloaderObjectModel;
            downloaderItem.Pause();
        }
        public bool Pause_CanExecute(object item)
        {
            if (item == null) return false;

            var dItem = item as DownloaderObjectModel;
            if (dItem.Status == DownloadStatus.Downloading)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        void Cancel(object item)
        {
            if (item == null) return;

            var downloaderItem = item as DownloaderObjectModel;
            downloaderItem.Cancel();
        }

        public bool Cancel_CanExecute(object item)
        {
            if (item == null) return false;

            var dItem = item as DownloaderObjectModel;
            if (dItem.Status == DownloadStatus.Downloading || dItem.Status == DownloadStatus.Paused)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        void Remove(object item)
        {
            if (item == null) return;
            var downloaderItem = item as DownloaderObjectModel;

            if (downloaderItem.Status == DownloadStatus.Downloading || downloaderItem.Status == DownloadStatus.Paused)
            {
                MessageBoxResult result = MessageBox.Show("Cancel downloading \"" + downloaderItem.Filename + "\" ?",
                    "Cancel Download", System.Windows.MessageBoxButton.YesNo);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
                else
                {
                    downloaderItem.Cancel();
                }

            }

            DownloadItemsList.Remove(downloaderItem);
        }

        public bool Remove_CanExecute(object item)
        {
            if (item == null) 
            { 
                return false; 
            }
            else 
            { 
                return true; 
            }
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
            if (item!=null )
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
            if (!QProcessor.IsProcessing && QProcessor.Count > 0)
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
            return (item != null || QProcessor.IsProcessing);
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
