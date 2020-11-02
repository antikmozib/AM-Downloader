// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

using System.Threading.Tasks;

namespace AMDownloader.QueueProcessing
{
    internal interface IQueueable
    {
        public Task StartAsync();

        public void Pause();

        public bool IsQueued { get; }
        public bool IsCompleted { get; }
    }
}