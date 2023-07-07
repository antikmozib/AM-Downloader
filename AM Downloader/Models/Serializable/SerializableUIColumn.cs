// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;

namespace AMDownloader.Models.Serializable
{
    [Serializable]
    public class SerializableUIColumn
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public double Width { get; set; }
    }

    [Serializable]
    public class SerializableUIColumnList
    {
        public List<SerializableUIColumn> Objects { get; set; }

        public SerializableUIColumnList()
        {
            Objects = new();
        }
    }
}
