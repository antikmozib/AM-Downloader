using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using AMDownloader.ObjectModel;

namespace AMDownloader.QueueProcessing
{    
    class QueueProcessor
    {
        #region Fields
        private readonly SemaphoreSlim _semaphore;
        private readonly BlockingCollection<IQueueable> _queueList;
        private readonly List<IQueueable> _itemsProcessing;
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;
        #endregion // Fields

        #region Properties
        public bool IsBusy => _ctsCancel != null;
        #endregion // Properties

        #region Constructors

        public QueueProcessor(int maxParallelDownloads)
        {
            _queueList = new BlockingCollection<IQueueable>();
            _semaphore = new SemaphoreSlim(maxParallelDownloads);
            _itemsProcessing = new List<IQueueable>();
        }
        #endregion // Constructors

        #region Private methods
        private async Task ProcessQueueAsync()
        {
            var tasks = new List<Task>();
            _ctsCancel = new CancellationTokenSource();
            _ctCancel = _ctsCancel.Token;

            while (_queueList.Count() > 0 && !_ctCancel.IsCancellationRequested)
            {
                IQueueable item;
                if (!_queueList.TryTake(out item)) break;
           
                if (!item.IsQueued) continue;
                _itemsProcessing.Add(item);
                Task t = Task.Run(async () =>
                {
                    await _semaphore.WaitAsync();
                    if (item.IsQueued && !_ctCancel.IsCancellationRequested)
                    {
                        await item.StartAsync();
                    }
                    _semaphore.Release();
                });
                tasks.Add(t);
            }

            await Task.WhenAll(tasks.ToArray());

            foreach (var item in _itemsProcessing)
            {
                if (!item.IsCompleted && !this.Contains(item))
                {
                    this.Add(item);
                }
            }

            _itemsProcessing.Clear();
            _ctsCancel = null;
            _ctCancel = default;
        }
        #endregion // Private methods

        #region Public methods
        // Producer
        public bool Add(IQueueable item)
        {
            if (item.IsCompleted || _queueList.Contains(item)) return true;
            return _queueList.TryAdd(item);
        }

        // Consumer
        public async Task StartAsync()
        {
            if (_ctsCancel != null) return;
            await ProcessQueueAsync();
        }

        public void Stop()
        {
            _ctsCancel?.Cancel();
            foreach (var item in _itemsProcessing)
            {
                item.Pause();
            }
        }
        #endregion // Public methods

        #region Public functions
        public bool Contains(IQueueable value)
        {
            return _queueList.Contains(value);
        }
        public int Count()
        {
            return _queueList.Count + _itemsProcessing.Count;
        }
        #endregion // Public functions
    }
}