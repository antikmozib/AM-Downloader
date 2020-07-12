using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Collections.ObjectModel;
using System.Linq;

namespace AMDownloader
{
    class QueueProcessor
    {
        private BlockingCollection<DownloaderObjectModel> _queueList;
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;

        public int Count
        {
            get
            {
                return _queueList.Count;
            }
        }

        public bool IsBusy
        {
            get
            {
                return (_ctsCancel != null);
            }
        }

        public QueueProcessor()
        {
            this._queueList = new BlockingCollection<DownloaderObjectModel>();
        }

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
            if (_ctsCancel != null)
            {
                return;
            }

            if (firstItems != null)
            {
                RecreateQueue(firstItems);
            }

            await ProcessQueueAsync();
        }

        public void Stop(ObservableCollection<DownloaderObjectModel> checkMainList = null)
        {
            if (_ctsCancel != null)
            {
                _ctsCancel.Cancel();
            }

            if (checkMainList != null)
            {
                var items = (from item in checkMainList
                             where item.Status == DownloaderObjectModel.DownloadStatus.Downloading
                             where item.QProcessor != null
                             select item).ToArray();

                Parallel.ForEach(items, (item) =>
                {
                    item.Pause();
                });

                RecreateQueue(items.ToArray());
            }
        }

        private async Task ProcessQueueAsync()
        {
            _ctsCancel = new CancellationTokenSource();
            _ctCancel = _ctsCancel.Token;

            while (!_ctCancel.IsCancellationRequested && _queueList.Count > 0)
            {
                try
                {
                    // Download max n items
                    DownloaderObjectModel[] items = { null, null, null };

                    for (int i = 0; i < items.Length; i++)
                    {
                        items[i] = null;

                        if (!_queueList.TryTake(out items[i]))
                        {
                            break;
                        }
                    }

                    List<Task> tasks = new List<Task>(items.Length);

                    foreach (DownloaderObjectModel item in items)
                    {
                        if (item != null)
                            tasks.Add(item.StartAsync());
                    }

                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _ctsCancel = null;
        }

        private void RecreateQueue(params DownloaderObjectModel[] firstItems)
        {
            if (this._queueList == null)
            {
                return;
            }

            if (_ctsCancel != null)
            {
                _ctsCancel.Cancel();
            }

            BlockingCollection<DownloaderObjectModel> newList = new BlockingCollection<DownloaderObjectModel>();

            foreach (DownloaderObjectModel obj in firstItems)
            {
                newList.Add(obj);
            }

            foreach (DownloaderObjectModel obj in _queueList)
            {
                DownloaderObjectModel item = null;
                if (_queueList.TryTake(out item))
                {
                    if (firstItems.Contains<DownloaderObjectModel>(item))
                    {
                        continue;
                    }
                    else
                    {
                        newList.Add(item);
                    }
                }
            }

            var disposeList = _queueList;
            _queueList = newList;
            disposeList.Dispose();
        }

        public bool Contains(DownloaderObjectModel value)
        {
            return (_queueList.Contains<DownloaderObjectModel>(value));
        }
    }
}
