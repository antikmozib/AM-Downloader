using System;
using System.Collections.Generic;
using System.Text;

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
        public class SerializableDownloadPathHistory
        {
            public string path { get; set; }
        }
    }
}
