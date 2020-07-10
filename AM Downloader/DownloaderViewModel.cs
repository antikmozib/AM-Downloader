using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Concurrent;

namespace AMDownloader
{
    class DownloaderViewModel
    {
        public HttpClient httpClient = new HttpClient();
        public ObservableCollection<DownloaderObjectModel> DownloadItemsList;
        public BlockingCollection<DownloaderObjectModel> QueueList;

        public RelayCommand AddCommand { get; set; }
        public RelayCommand StartCommand { get; set; }
        public RelayCommand RemoveCommand { private get; set; }
        public RelayCommand CancelCommand { private get; set; }
        public RelayCommand PauseCommand { get; private set; }
        
        public DownloaderViewModel()
        {
            DownloadItemsList = new ObservableCollection<DownloaderObjectModel>();
            QueueList = new BlockingCollection<DownloaderObjectModel>();
            AddCommand = new RelayCommand(Add);
            StartCommand = new RelayCommand(Start);
            RemoveCommand = new RelayCommand(Remove);
            CancelCommand = new RelayCommand(Cancel);
            PauseCommand = new RelayCommand(Pause);
        }

        void Start(object item)
        {
            if (item == null) return;

            var downloaderItem = item as DownloaderObjectModel;
            Task.Run(() => downloaderItem.StartAsync());
        }

        void Pause(object item)
        {
            if (item == null) return;

            DownloaderObjectModel downloaderItem = item as DownloaderObjectModel;
            downloaderItem.Pause();
        }

        void Cancel(object item)
        {
            if (item == null) return;

            var downloaderItem = item as DownloaderObjectModel;
            downloaderItem.Cancel();
        }

        void Remove(object item)
        {
            if (item == null) return;
            var downloaderItem = item as DownloaderObjectModel;

            if (downloaderItem.Status == DownloaderObjectModel.DownloadStatus.Downloading || downloaderItem.Status==DownloaderObjectModel.DownloadStatus.Paused)
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

        void Add(object item)
        {
            AddDownloadViewModel addDownloadViewModel = new AddDownloadViewModel(this);
            AddDownloadWindow addDownloadWindow = new AddDownloadWindow();
            addDownloadWindow.DataContext = addDownloadViewModel;

            addDownloadViewModel.Urls = @"https://download-installer.cdn.mozilla.net/pub/firefox/releases/77.0.1/win64/en-US/Firefox%20Setup%2077.0.1.exe" + '\n' + @"https://download3.operacdn.com/pub/opera/desktop/69.0.3686.49/win/Opera_69.0.3686.49_Setup_x64.exe";
            addDownloadViewModel.SaveToFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            addDownloadViewModel.AddToQueue = true;
            addDownloadViewModel.StartDownload = false;

            addDownloadWindow.Owner = item as Window;
            addDownloadWindow.ShowDialog();
        }
    }
}
