// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Generic;

namespace AMDownloader.ObjectModel.Serializable
{
    [Serializable]
    public class SerializableUIColumnOrder
    {
        public string ColumnName { get; set; }
        public int ColumnIndex { get; set; }
    }

    [Serializable]
    public class SerializableUIColumnOrderList
    {
        public List<SerializableUIColumnOrder> Objects;

        public SerializableUIColumnOrderList()
        {
            Objects = new();
        }
    }
}
