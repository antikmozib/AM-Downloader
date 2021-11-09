// Copyright (C) 2020-2021 Antik Mozib.

using System;
using System.Collections.Generic;

namespace AMDownloader.ObjectModel.Serializable
{
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