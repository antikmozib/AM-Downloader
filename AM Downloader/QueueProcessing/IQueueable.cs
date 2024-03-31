// Copyright (C) 2020-2024 Antik Mozib. All rights reserved.

using System.Threading.Tasks;

namespace AMDownloader.QueueProcessing
{
    public interface IQueueable
    {
        Task StartAsync();
        void Pause();
        bool IsCompleted { get; }
        bool IsErrored { get; }
    }
}