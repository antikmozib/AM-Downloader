// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System.Threading.Tasks;

namespace AMDownloader.QueueProcessing
{
    public interface IQueueable
    {
        string Id { get; }
        Task StartAsync();
        void Pause();
        bool IsCompleted { get; }
        bool IsErrored { get; }
    }
}