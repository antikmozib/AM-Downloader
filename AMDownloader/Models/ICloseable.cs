﻿// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System;

namespace AMDownloader.Models
{
    public interface ICloseable
    {
        event EventHandler Closing;
        event EventHandler Closed;
        void Close();
    }
}
