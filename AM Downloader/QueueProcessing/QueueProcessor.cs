// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMDownloader.QueueProcessing
{
    internal class QueueProcessor : INotifyPropertyChanged
    {
        #region Fields

        private readonly SemaphoreSlim _semaphore;
        private readonly List<IQueueable> _itemsProcessing;
        private BlockingCollection<IQueueable> _queueList;
        private CancellationTokenSource _ctsCancelProcessingQueue;
        private CancellationToken _ctCancelProcessingQueue;

        #endregion Fields

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ItemEnqeued;
        public event EventHandler ItemDequeued;

        #endregion Events

        #region Properties

        public bool IsBusy => _ctCancelProcessingQueue != default;
        public bool HasItems { get; private set; }

        #endregion Properties

        #region Constructors

        public QueueProcessor(
            int maxParallelDownloads,
            PropertyChangedEventHandler propertyChangedEventHandler,
            EventHandler itemEnqueuedEventHandler,
            EventHandler itemDequeuedEventHandler)
        {
            _queueList = new BlockingCollection<IQueueable>(new ConcurrentQueue<IQueueable>());
            _semaphore = new SemaphoreSlim(maxParallelDownloads);
            _itemsProcessing = new List<IQueueable>();
            _ctsCancelProcessingQueue = null;

            this.HasItems = false;
            this.PropertyChanged += propertyChangedEventHandler;
            this.ItemEnqeued += itemEnqueuedEventHandler;
            this.ItemDequeued += itemDequeuedEventHandler;
        }

        public QueueProcessor(
            int maxParallelDownloads,
            PropertyChangedEventHandler propertyChangedEventHandler) : this(
                maxParallelDownloads,
                propertyChangedEventHandler,
                null,
                null)
        {
        }

        #endregion Constructors

        #region Private methods

        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }

        private async Task ProcessQueueAsync()
        {
            var tasks = new List<Task>();

            // add paused items back to the queue list
            foreach (var item in _itemsProcessing)
            {
                if (!item.IsCompleted && item.IsQueued && !_queueList.Contains(item))
                {
                    _queueList.TryAdd(item);
                }
            }

            _itemsProcessing.Clear();

            while (true)
            {
                if (!_queueList.TryTake(out IQueueable item)) break;
                if (!item.IsQueued) continue;
                _itemsProcessing.Add(item);

                Task t = Task.Run(async () =>
                {
                    var semTask = _semaphore.WaitAsync(_ctCancelProcessingQueue);
                    try
                    {
                        await semTask;
                        if (item.IsQueued)
                        {
                            await item.StartAsync();
                        }
                    }
                    finally
                    {
                        if (semTask.IsCompletedSuccessfully)
                        {
                            _semaphore.Release();
                        }
                    }

                    _ctCancelProcessingQueue.ThrowIfCancellationRequested();
                }, _ctCancelProcessingQueue);

                tasks.Add(t);
            }

            try
            {
                await Task.WhenAll(tasks.ToArray());
            }
            catch (OperationCanceledException)
            {
                _ctCancelProcessingQueue.ThrowIfCancellationRequested();
            }
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion Private methods

        #region Public methods

        // Producer
        public bool Add(IQueueable item)
        {
            if (item.IsCompleted || _queueList.Contains(item)) return true;
            if (_queueList.TryAdd(item))
            {
                if (!HasItems)
                {
                    HasItems = true;
                    RaisePropertyChanged(nameof(this.HasItems));
                }
                RaiseEvent(ItemEnqeued);
                return true;
            }
            return false;
        }

        public void Remove(params IQueueable[] items)
        {
            bool itemsAdded = false;

            if (items.Length == 1 && !_queueList.Contains(items[0]))
            {
                return;
            }

            var newList = new BlockingCollection<IQueueable>();

            foreach (var oldItem in _queueList)
            {
                if (items.Contains(oldItem))
                {
                    continue;
                }
                if (newList.TryAdd(oldItem))
                {
                    itemsAdded = true;
                }
            }

            _queueList.Dispose();
            _queueList = newList;

            if (itemsAdded)
            {
                RaiseEvent(ItemDequeued);
            }
        }

        // Consumer
        public async Task StartAsync()
        {
            if (this.IsBusy) return;

            _ctsCancelProcessingQueue = new CancellationTokenSource();
            _ctCancelProcessingQueue = _ctsCancelProcessingQueue.Token;

            try
            {
                await ProcessQueueAsync();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _ctsCancelProcessingQueue.Dispose();
                _ctsCancelProcessingQueue = null;
                _ctCancelProcessingQueue = default;
            }

            if (_queueList.Count == 0)
            {
                this.HasItems = false;
                RaisePropertyChanged(nameof(this.HasItems));
            }
        }

        public void Stop()
        {
            try
            {
                _ctsCancelProcessingQueue?.Cancel();
            }
            catch (ObjectDisposedException) { }

            // pause items being downloaded
            Parallel.ForEach(_itemsProcessing, (item) => item.Pause());
        }

        #endregion Public methods

        #region Public functions

        public bool Contains(IQueueable value)
        {
            return _queueList.Contains(value);
        }

        #endregion Public functions
    }
}