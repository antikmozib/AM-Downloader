// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;

namespace AMDownloader.Models.Serialization
{
    [Serializable]
    public class SerializingSavedLocation
    {
        public string Path { get; set; }
    }

    [Serializable]
    public class SerializingSavedLocationList
    {
        public List<SerializingSavedLocation> Objects { get; set; }

        public SerializingSavedLocationList()
        {
            Objects = new List<SerializingSavedLocation>();
        }
    }
}