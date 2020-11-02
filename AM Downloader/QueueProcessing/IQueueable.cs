// Copyright (C) 2020 Antik Mozib. Released under CC BY-NC-SA 4.0.

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