using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace AMDownloader
{
    class QueueProcessor
    {
        private BlockingCollection<DownloaderObjectModel> _queueList;
        private readonly List<DownloaderObjectModel> _itemsProcessing;
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;
        private readonly SemaphoreSlim _semaphore;

        #region Properties

        public bool IsBusy { get { return (_ctsCancel != null); } }

        #endregion

        #region Constructors

        public QueueProcessor() : this(2) { }

        public QueueProcessor(int maxParallelDownloads)
        {
            _queueList = new BlockingCollection<DownloaderObjectModel>();
            _itemsProcessing = new List<DownloaderObjectModel>();
            _semaphore = new SemaphoreSlim(maxParallelDownloads);
        }

        #endregion

        #region Private methods

        private async Task ProcessQueueAsync()
        {
            var tasks = new List<Task>();

            _ctsCancel = new CancellationTokenSource();
            _ctCancel = _ctsCancel.Token;
            _itemsProcessing.Clear();

            while (_queueList.Count() > 0 && !_ctCancel.IsCancellationRequested)
            {
                DownloaderObjectModel item;
                if (!_queueList.TryTake(out item)) break;

                if (!item.IsQueued) continue;

                Task t = Task.Run(async () =>
                {
                    _itemsProcessing.Add(item);
                    _semaphore.Wait();

                    if (!_ctCancel.IsCancellationRequested && item.IsQueued)
                    {
                        await item.StartAsync();
                    }

                    _semaphore.Release();
                    _itemsProcessing.Remove(item);
                });

                tasks.Add(t);
            }

            await Task.WhenAll(tasks.ToArray());

            _ctsCancel = null;
            _ctCancel = default;
        }

        private void RecreateQueue(params DownloaderObjectModel[] addToTop)
        {
            if (this._queueList == null) return;

            _ctsCancel?.Cancel();

            var newList = new BlockingCollection<DownloaderObjectModel>();

            foreach (var item in addToTop)
                newList.Add(item);

            while (_queueList.Count > 0)
            {
                DownloaderObjectModel item;

                if (_queueList.TryTake(out item))
                {
                    if (addToTop.Contains(item))
                        continue;
                    else
                        newList.Add(item);
                }
                else
                {
                    break;
                }
            }

            var disposeList = _queueList;
            _queueList = newList;
            disposeList.Dispose();
        }

        #endregion

        #region Public methods

        // Producer
        public void Add(DownloaderObjectModel item)
        {
            if (_queueList.Contains(item)) return;

            try
            {
                _queueList.TryAdd(item);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Consumer
        public async Task StartAsync(params DownloaderObjectModel[] firstItems)
        {
            if (_ctsCancel != null) return;
            if (firstItems != null) RecreateQueue(firstItems);

            await ProcessQueueAsync();
        }

        public void Stop()
        {
            _ctsCancel?.Cancel();

            // items that are being downloaded were taken out of queue; add them back to the top
            if (_itemsProcessing.Count > 0)
            {
                Parallel.ForEach(_itemsProcessing, (item) => { item.Pause(); });
                RecreateQueue(_itemsProcessing.ToArray());
            }
        }

        #endregion

        #region Public functions

        public bool Contains(DownloaderObjectModel value)
        {
            return (_queueList.Contains(value));
        }

        public int Count()
        {
            return _queueList.Count();
        }

        #endregion
    }
}