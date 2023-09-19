// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;

namespace AMDownloader.Models.Serialization
{
    [Serializable]
    public class SerializingDownloaderObjectModel
    {
        public int Index { get; set; }
        public string Url { get; set; }
        public string Destination { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? CompletedOn { get; set; }
        public long? TotalBytesToDownload { get; set; }
        public int ConnLimit { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
        public DownloadStatus Status { get; set; }
        public bool IsQueued { get; set; }
    }

    [Serializable]
    public class SerializingDownloaderObjectModelList
    {
        public List<SerializingDownloaderObjectModel> Objects { get; set; }

        public SerializingDownloaderObjectModelList()
        {
            Objects = new List<SerializingDownloaderObjectModel>();
        }
    }
}