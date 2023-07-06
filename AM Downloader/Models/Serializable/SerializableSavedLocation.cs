// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;

namespace AMDownloader.Models.Serializable
{
    [Serializable]
    public class SerializableSavedLocation
    {
        public string Path { get; set; }
    }

    [Serializable]
    public class SerializableSavedLocationList
    {
        public List<SerializableSavedLocation> Objects;

        public SerializableSavedLocationList()
        {
            Objects = new List<SerializableSavedLocation>();
        }
    }
}