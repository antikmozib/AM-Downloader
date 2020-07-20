using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace AMDownloader
{
    class QueueProcessor
    {
        private ObservableCollection<DownloaderObjectModel> _mainDownloadsList;
        private BlockingCollection<DownloaderObjectModel> _queueList;
        private List<DownloaderObjectModel> _itemsProcessing;
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;

        #region Properties

        public bool IsBusy { get { return (_ctsCancel != null); } }

        #endregion

        #region Constructors

        public QueueProcessor(ObservableCollection<DownloaderObjectModel> mainDownloadsList)
        {
            this._mainDownloadsList = mainDownloadsList;
            this._queueList = new BlockingCollection<DownloaderObjectModel>();
        }

        #endregion

        #region Private methods

        private async Task ProcessQueueAsync()
        {
            _ctsCancel = new CancellationTokenSource();
            _ctCancel = _ctsCancel.Token;
            _itemsProcessing = new List<DownloaderObjectModel>();

            while (!_ctCancel.IsCancellationRequested && _queueList.Count > 0)
            {
                try
                {
                    // Download max n items
                    DownloaderObjectModel[] items = { null, null, null };

                    int itemsAdded = 0;

                    while (itemsAdded < items.Count<DownloaderObjectModel>())
                    {
                        items[itemsAdded] = null;

                        if (_ctCancel.IsCancellationRequested || !_queueList.TryTake(out items[itemsAdded])) break;
                        if (!items[itemsAdded].IsQueued || !_mainDownloadsList.Contains(items[itemsAdded])) continue;

                        itemsAdded++;
                    }

                    List<Task> tasks = new List<Task>(items.Length);

                    foreach (var item in items)
                    {
                        if (item != null)
                        {
                            tasks.Add(item.StartAsync());

                            // keep a temporary reference to each item we are processing
                            _itemsProcessing.Add(item);
                        }
                    }

                    await Task.WhenAll(tasks);

                    // Download complete; remove from processing list
                    foreach (var item in items)
                        if (item != null) _itemsProcessing.Remove(item);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _ctsCancel = null;
            _ctCancel = default;
        }

        private void RecreateQueue(params DownloaderObjectModel[] firstItems)
        {
            if (this._queueList == null) return;

            _ctsCancel?.Cancel();

            var newList = new BlockingCollection<DownloaderObjectModel>();

            foreach (var obj in firstItems)
                newList.Add(obj);

            foreach (var obj in _queueList)
            {
                DownloaderObjectModel item = null;
                if (_queueList.TryTake(out item))
                {
                    if (firstItems.Contains<DownloaderObjectModel>(item))
                        continue;
                    else
                        newList.Add(item);
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
            bool success = false;

            try
            {
                success = _queueList.TryAdd(item);
            }
            catch (OperationCanceledException)
            {
                _queueList.CompleteAdding();
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

            if (_itemsProcessing != null)
            {
                // Pause all downloads started as part of the queue
                Parallel.ForEach(_itemsProcessing, (item) =>
                {
                    item.Pause();
                });

                // Items that were being downloaded were removed from the queue; add them back
                RecreateQueue(_itemsProcessing.ToArray());
            }
        }

        #endregion

        #region Public functions

        public bool Contains(DownloaderObjectModel value)
        {
            return (_queueList.Contains<DownloaderObjectModel>(value));
        }

        public int Count()
        {
            return _queueList.Count();
        }

        #endregion
    }
}
