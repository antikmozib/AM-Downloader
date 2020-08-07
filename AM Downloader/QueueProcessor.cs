using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;

namespace AMDownloader
{
    // Represents contracts that can be used by the QueueProcessor
    public interface IQueueable
    {
        public Task StartAsync(int numStreams); // numStreams = max num of parallel streams per IQueueable
        public void Pause();
        public bool IsQueued { get; }
        public bool IsCompleted { get; }
    }

    class QueueProcessor
    {
        #region Fields
        private const int DEFAULT_MAX_PARALLEL_DOWNLOADS = 5;
        private readonly SemaphoreSlim _semaphore;
        private BlockingCollection<IQueueable> _queueList;
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;
        private List<IQueueable> _itemsProcessing;
        #endregion // Fields

        #region Properties
        public bool IsBusy { get { return (_ctsCancel != null); } }
        #endregion // Properties

        #region Constructors

        public QueueProcessor(int maxParallelDownloads = DEFAULT_MAX_PARALLEL_DOWNLOADS)
        {
            _queueList = new BlockingCollection<IQueueable>();
            _semaphore = new SemaphoreSlim(maxParallelDownloads);
            _itemsProcessing = new List<IQueueable>();
        }
        #endregion // Constructors

        #region Private methods
        private async Task ProcessQueueAsync(int numStreams)
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
                    _semaphore.Wait();
                    if (!_ctCancel.IsCancellationRequested && item.IsQueued) await item.StartAsync(numStreams);
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
        public void Add(IQueueable item)
        {
            if (_queueList.Contains(item)) return;
            _queueList.TryAdd(item);
        }

        // Consumer
        public async Task StartAsync(int numStreams)
        {
            if (_ctsCancel != null) return;

            await ProcessQueueAsync(numStreams);
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
            return (_queueList.Contains(value));
        }
        public int Count()
        {
            return _queueList.Count + _itemsProcessing.Count;
        }
        #endregion // Public functions
    }
}