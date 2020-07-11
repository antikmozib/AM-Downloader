using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static AMDownloader.Shared;
using System.Windows;
using System.Linq;
using System.Threading.Tasks;

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

                // Get rid of empty strings
                IEnumerable<string> filteredUrls = from url
                                                   in Urls.Split('\n').ToList<string>()
                                                   where url.Trim().Length > 0
                                                   select url;

                foreach (var url in filteredUrls)
                {
                    var fileName = GetValidFilename(SaveToFolder + Path.GetFileName(url), parentViewModel.DownloadItemsList);
                    DownloaderObjectModel dItem;

                    if (AddToQueue)
                    {
                        dItem = new DownloaderObjectModel(ref parentViewModel.Client, url, fileName, parentViewModel.QProcessor);
                        parentViewModel.QProcessor.Add(dItem);
                    }
                    else
                    {
                        dItem = new DownloaderObjectModel(ref parentViewModel.Client, url, fileName, null);
                    }

                    parentViewModel.DownloadItemsList.Add(dItem);

                    if (!AddToQueue && StartDownload)
                    {
                        Task.Run(async () => await dItem.StartAsync());
                    }
                }

                if (AddToQueue && StartDownload)
                {
                    Task.Run(async () => await parentViewModel.QProcessor.StartAsync());
                }
            }
        }
    }
}
