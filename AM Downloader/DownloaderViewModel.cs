using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
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

        public string TotalSize(object item)
        {
            DownloaderItemModel dItem = item as DownloaderItemModel;
            return (dItem.TotalBytesToDownload / (1024 * 1024)).ToString() + " MB";
        }

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
            Task.Run(() => downloaderItem.StartAsync());
        }

        void Pause(object item)
        {
            if (item == null) return;

            DownloaderItemModel downloaderItem = item as DownloaderItemModel;
            downloaderItem.Pause();
        }

        void Cancel(object item)
        {
            if (item == null) return;

            var downloaderItem = item as DownloaderItemModel;
            downloaderItem.Cancel();
        }

        void Remove(object item)
        {
            if (item == null) return;
            var downloaderItem = item as DownloaderItemModel;

            if (downloaderItem.Status == DownloaderItemModel.DownloadStatus.Downloading)
            {
                MessageBoxResult result = MessageBox.Show("Cancel downloading \"" + downloaderItem.Filename + "\" ?", "Cancel Download", System.Windows.MessageBoxButton.YesNo);

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
    }
}
