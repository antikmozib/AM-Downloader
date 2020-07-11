using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Windows.Documents;
using System.Collections.ObjectModel;
using System.Linq;

namespace AMDownloader
{
    class QueueProcessor
    {
        private BlockingCollection<DownloaderObjectModel> _queueList;
        private CancellationTokenSource _ctsCancel;
        private CancellationToken _ctCancel;

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

        public void Stop(ObservableCollection<DownloaderObjectModel> CancelThese = null)
        {
            if (_ctsCancel != null)
            {
                _ctsCancel.Cancel();
            }

            if (CancelThese != null)
            {
                var items = from item in CancelThese where item.Status == DownloaderObjectModel.DownloadStatus.Downloading select item;
                Parallel.ForEach(items, (item) => item.Cancel());
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
                    // Download max 5 times
                    DownloaderObjectModel[] items = { null, null, null, null, null };

                    for (int i = 0; i < items.Length; i++)
                    {
                        items[i] = null;

                        if (!_queueList.TryTake(out items[i], 1000, _ctCancel))
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

        private void RecreateQueue(DownloaderObjectModel firstItem)
        {
            if (this._queueList == null)// || _ctsCancel != null)
            {
                return;
            }

            if (_ctsCancel != null)
            {
                _ctsCancel.Cancel();
            }

            BlockingCollection<DownloaderObjectModel> newList = new BlockingCollection<DownloaderObjectModel>();

            newList.Add(firstItem);

            foreach (DownloaderObjectModel objects in _queueList)
            {
                DownloaderObjectModel item = null;
                if (_queueList.TryTake(out item))
                {
                    if (item == firstItem)
                    {
                        continue;
                    }
                    else
                    {
                        newList.Add(item);
                    }
                }
            }

            _queueList.Dispose();
            _queueList = newList;
        }
    }
}
