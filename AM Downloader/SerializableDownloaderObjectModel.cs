using System;
using System.Collections.Generic;
using System.Text;

namespace AMDownloader
{
    [Serializable]
    public class SerializableDownloaderObjectModel
    {
        public string Url { get; set; }
        public string Destination { get; set; }
        public bool IsQueued { get; set; }
        public DateTime DateCreated { get; set; }
    }
}
