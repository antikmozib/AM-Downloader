using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using static AM_Downloader.DownloaderModels;

namespace AM_Downloader
{
    class DownloaderViewModel
    {
        public HttpClient httpClient = new HttpClient();
        public ObservableCollection<DownloaderItemModel> DownloadItemsList;

        public RelayCommand AddCommand { get; set; }
        public RelayCommand StartCommand { get; set; }
        public RelayCommand RemoveCommand { private get; set; }
        public RelayCommand CancelCommand { private get; set; }
        public RelayCommand PauseCommand { get; private set; }

        public DownloaderViewModel()
        {
            DownloadItemsList = new ObservableCollection<DownloaderItemModel>();
            StartCommand = new RelayCommand(Start);
            RemoveCommand = new RelayCommand(Remove);
            CancelCommand = new RelayCommand(Cancel);
            PauseCommand = new RelayCommand(Pause);
        }

        void Start(object item)
        {
            if (item == null) return;

            var downloaderItem = item as DownloaderItemModel;
            downloaderItem.Start();
        }

        void Pause(object item)
        {
            if (item == null) return;

            DownloaderItemModel downloaderItem = item as DownloaderItemModel;
            downloaderItem.PauseAsync();
        }

        void Cancel(object item)
        {
            if (item == null) return;

            var downloaderItem = item as DownloaderItemModel;
            _ = downloaderItem.CancelAsync();
        }

        void Remove(object item)
        {
            if (item == null) return;

            bool deleteFile = false;
            var downloaderItem = item as DownloaderItemModel;

            if (File.Exists(downloaderItem.LongFilename))
            {
                MessageBoxResult result = MessageBox.Show("Delete file \"" + Path.GetFileName(downloaderItem.LongFilename) + "\" from disk?", "Remove", System.Windows.MessageBoxButton.YesNoCancel);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    deleteFile = true;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            DownloadItemsList.Remove(downloaderItem);

            if (deleteFile && File.Exists(downloaderItem.LongFilename))
            {
                File.Delete(downloaderItem.LongFilename);
            }
        }
    }
}
