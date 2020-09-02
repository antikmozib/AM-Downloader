// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

using System.Threading.Tasks;

namespace AMDownloader.QueueProcessing
{
    interface IQueueable
    {
        public Task StartAsync();
        public void Pause();
        public bool IsQueued { get; }
        public bool IsCompleted { get; }
    }
}
