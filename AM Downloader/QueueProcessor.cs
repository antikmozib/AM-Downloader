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

        public bool IsProcessing
        {
            get
            {
                return (_ctsCancel != null);
            }
        }

        public BlockingCollection<DownloaderObjectModel> QueueList
        {
            get
            {
                return _queueList;
            }
        }

        public QueueProcessor(ref BlockingCollection<DownloaderObjectModel> queueList)
        {
            this._queueList = queueList;
        }

        // Producer
        public void Add(DownloaderObjectModel item)
        {
            bool success = false;

            try
            {
                success = _queueList.TryAdd(item, 1000);
            }
            catch (OperationCanceledException)
            {
                _queueList.CompleteAdding();
            }
        }

        // Consumer
        public async Task StartAsync(DownloaderObjectModel StartWithThis = null)
        {
            if (_ctsCancel != null)
            {
                return;
            }

            if (StartWithThis != null)
            {
                RecreateQueue(StartWithThis);
            }

            await ProcessQueue();
        }

        public void Stop(ObservableCollection<DownloaderObjectModel> CheckMainList = null)
        {
            if (_ctsCancel != null)
            {
                _ctsCancel.Cancel();
            }

            if (CheckMainList != null)
            {
                var items = (from item in CheckMainList
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

        private async Task ProcessQueue()
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

            var _disposeList = _queueList;
            _queueList = newList;
            _disposeList.Dispose();
        }
    }
}
