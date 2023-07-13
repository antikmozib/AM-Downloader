// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;

namespace AMDownloader.Models.Serialization
{
    [Serializable]
    public class SerializingUIColumn
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public double Width { get; set; }
    }

    [Serializable]
    public class SerializingUIColumnList
    {
        public List<SerializingUIColumn> Objects { get; set; }

        public SerializingUIColumnList()
        {
            Objects = new();
        }
    }
}
