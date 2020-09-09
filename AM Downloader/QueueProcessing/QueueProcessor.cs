// Copyright (C) 2020 Antik Mozib. Released under GNU GPLv3.

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
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;
        private bool _hasItems;

        #endregion Fields

        #region Properties

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsBusy => _ctsCancel != null;
        public bool HasItems => _hasItems;

        #endregion Properties

        #region Constructors

        public QueueProcessor(int maxParallelDownloads, PropertyChangedEventHandler propertyChangedEventHandler)
        {
            _queueList = new BlockingCollection<IQueueable>();
            _semaphore = new SemaphoreSlim(maxParallelDownloads);
            _itemsProcessing = new List<IQueueable>();
            _hasItems = false;
            this.PropertyChanged += propertyChangedEventHandler;
        }

        #endregion Constructors

        #region Private methods

        private async Task ProcessQueueAsync()
        {
            var tasks = new List<Task>();
            _ctsCancel = new CancellationTokenSource();
            _ctCancel = _ctsCancel.Token;
            RaisePropertyChanged(nameof(this.IsBusy));

            while (!_ctCancel.IsCancellationRequested)
            {
                IQueueable item;
                if (!_queueList.TryTake(out item)) break;

                if (!item.IsQueued) continue;
                _itemsProcessing.Add(item);
                Task t = Task.Run(async () =>
                {
                    try
                    {
                        await _semaphore.WaitAsync(_ctCancel);
                        if (item.IsQueued && !_ctCancel.IsCancellationRequested)
                        {
                            await item.StartAsync();
                        }
                        _semaphore.Release();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                });
                tasks.Add(t);
            }

            await Task.WhenAll(tasks.ToArray());

            if (_ctCancel.IsCancellationRequested)
            {
                var newList = new BlockingCollection<IQueueable>();
                foreach (var item in _itemsProcessing)
                {
                    if (!item.IsCompleted && !_queueList.Contains(item) && item.IsQueued)
                    {
                        newList.Add(item);
                    }
                }
                foreach (var item in _queueList)
                {
                    newList.Add(item);
                }
                _queueList.Dispose();
                _queueList = newList;
            }

            _itemsProcessing.Clear();
            _ctsCancel = null;
            _ctCancel = default;
            RaisePropertyChanged(nameof(this.IsBusy));
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
                if (!_hasItems)
                {
                    _hasItems = true;
                    RaisePropertyChanged(nameof(this.HasItems));
                }
                return true;
            }
            return false;
        }

        public void Remove(params IQueueable[] items)
        {
            if (items.Count() == 1 && !_queueList.Contains(items[0]))
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
                newList.TryAdd(oldItem);
            }

            _queueList.Dispose();
            _queueList = newList;
        }

        // Consumer
        public async Task StartAsync()
        {
            if (_ctsCancel != null) return;
            await ProcessQueueAsync();
            if (_queueList.Count == 0)
            {
                _hasItems = false;
                RaisePropertyChanged(nameof(this.HasItems));
            }
        }

        public void Stop()
        {
            _ctsCancel?.Cancel();
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