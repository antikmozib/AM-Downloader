// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;

namespace AMDownloader.Models.Serializable
{
    [Serializable]
    public class SerializableDownloaderObjectModel
    {
        public int Index { get; set; }
        public string Url { get; set; }
        public string Destination { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateFinished { get; set; }
        public long? TotalBytesToDownload { get; set; }
        public int ConnLimit { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
        public DownloadStatus Status { get; set; }
        public bool IsQueued { get; set; }
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
}