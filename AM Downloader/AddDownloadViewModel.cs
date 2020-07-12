using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static AMDownloader.Shared;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AMDownloader
{
    class AddDownloadViewModel
    {
        private DownloaderViewModel parentViewModel;
        public string Urls { get; set; }
        public string SaveToFolder { get; set; }
        public bool StartDownload { get; set; }
        public bool AddToQueue { get; set; }

        public RelayCommand AddCommand { get; private set; }
        public RelayCommand PreviewCommand { get; private set; }

        public AddDownloadViewModel(DownloaderViewModel parent)
        {
            parentViewModel = parent;
            AddCommand = new RelayCommand(Add);
            PreviewCommand = new RelayCommand(Preview);
        }

        public void Preview(object item)
        {
            foreach (var url in ListifyUrls())
            {
                Debug.WriteLine(url);
            }
        }

        public void Add(object item)
        {

            if (Urls == null || SaveToFolder == null || !Directory.Exists(SaveToFolder))
            {
                return;
            }

            if (SaveToFolder.LastIndexOf(Path.DirectorySeparatorChar) != SaveToFolder.Length - 1)
            {
                SaveToFolder = SaveToFolder + Path.DirectorySeparatorChar;
            }

            foreach (var url in ListifyUrls())
            {
                AddItemToList(url);
            }

            if (AddToQueue && StartDownload)
            {
                Task.Run(async () => await parentViewModel.QProcessor.StartAsync());
            }
        }

        private void AddItemToList(string url)
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

            if (!AddToQueue && StartDownload)
            {
                Task.Run(async () => await dItem.StartAsync());
            }

            parentViewModel.DownloadItemsList.Add(dItem);
        }

        private List<string> ListifyUrls()
        {
            string pattern = @"[[]\d+[:]\d+[]]";
            List<string> urlList = new List<string>();

            foreach (var url in Urls.Split('\n').ToList<string>())
            {
                if (url.Trim().Length == 0)
                {
                    // empty line
                    continue;
                }
                else if (Regex.Match(url, pattern).Success)
                {
                    // url has patterns
                    string bounds = Regex.Match(url, pattern).Value;
                    int lBound = 0, uBound = 0;

                    int.TryParse(bounds.Substring(1, bounds.IndexOf(':') - 1), out lBound);
                    int.TryParse(bounds.Substring(bounds.IndexOf(':') + 1, bounds.Length - bounds.IndexOf(':') - 2), out uBound);

                    for (int i = lBound; i <= uBound; i++)
                    {
                        var newurl = url.Replace(bounds, i.ToString());
                        urlList.Add(newurl);
                    }
                }
                else
                {
                    // normal url
                    urlList.Add(url);
                }
            }

            return urlList;
        }
    }
}
