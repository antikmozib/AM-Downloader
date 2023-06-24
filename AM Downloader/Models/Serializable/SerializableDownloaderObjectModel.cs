﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

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
        public long? TotalBytesToDownload { get; set; }
        public int ConnectionLimit { get; set; }
        public bool IsQueued { get; set; }
        public DownloadStatus Status { get; set; }
        public DateTime DateCreated { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
    }

    [Serializable]
    public class SerializableDownloaderObjectModelList
    {
        public List<SerializableDownloaderObjectModel> Objects;
        public int Count => Objects.Count;

        public SerializableDownloaderObjectModelList()
        {
            Objects = new List<SerializableDownloaderObjectModel>();
        }
    }
}