using System;
using System.Collections.Generic;

namespace AMDownloader
{
    public static class SerializableModels
    {
        [Serializable]
        public class SerializableDownloaderObjectModel
        {
            public string Url { get; set; }
            public string Destination { get; set; }
            public bool IsQueued { get; set; }
            public DateTime DateCreated { get; set; }
        }
        [Serializable]
        public class SerializableDownloaderObjectModelList
        {
            public List<SerializableDownloaderObjectModel> Objects;
            public SerializableDownloaderObjectModelList()
            {
                Objects = new List<SerializableDownloaderObjectModel>();
            }
        }
        [Serializable]
        public class SerializableDownloadPathHistory
        {
            public string path { get; set; }
        }
        [Serializable]
        public class SerializableDownloadPathHistoryList
        {
            public List<SerializableDownloadPathHistory> Objects;
            public SerializableDownloadPathHistoryList()
            {
                Objects = new List<SerializableDownloadPathHistory>();
            }
        }
    }
}
