using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net.Http;
using static AMDownloader.Shared;
using System.Windows;
using System.Linq;

namespace AMDownloader
{
    class AddDownloadViewModel
    {
        private DownloaderViewModel parentViewModel;

        public string Urls { get; set; }
        public string SaveToFolder { get; set; }
        public bool StartDownload { get; set; }
        public bool AddToQueue { get; set; }

        public RelayCommand AddCommand { private get; set; }

        public AddDownloadViewModel(DownloaderViewModel parent)
        {
            parentViewModel = parent;
            AddCommand = new RelayCommand(Add);
        }

        public void Add(object item)
        {            
            if (Urls == null || SaveToFolder == null || !Directory.Exists(SaveToFolder))
            {
                return;
            }
            {
                if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
                {
                    SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;
                }

                foreach (var url in Urls.Split('\n').ToList<string>())
                {
                    try
                    {

                        var dlItem = new DownloaderObjectModel(ref parentViewModel.httpClient, 
                            url, GetValidFilename(SaveToFolder + Path.GetFileName(url)), StartDownload, AddToQueue);

                        parentViewModel.DownloadItemsList.Add(dlItem);

                        if (AddToQueue)
                        {
                            parentViewModel.QueueList.Add(dlItem);
                        }
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(e.Message);
                    }
                }
            }
        }
    }
}
