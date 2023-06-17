// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;

namespace AMDownloader.Models.Serializable
{
    [Serializable]
    public class SerializableDownloadPathHistory
    {
        public string FolderPath { get; set; }
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