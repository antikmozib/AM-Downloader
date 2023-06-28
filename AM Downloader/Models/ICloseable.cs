// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Threading.Tasks;

namespace AMDownloader.Models
{
    internal interface ICloseable
    {
        event EventHandler Closing;
        event EventHandler Closed;
        void Close();
    }
}
