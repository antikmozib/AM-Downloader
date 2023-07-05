// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using Serilog;
using System;
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

        private readonly List<IQueueable> _queueList;
        private readonly List<IQueueable> _processedItems;
        private readonly object _queueListLock;
        private readonly object _processedItemsLock;
        private readonly SemaphoreSlim _semaphore;
        private TaskCompletionSource _tcs;
        private CancellationTokenSource _cts;

        #endregion

        #region Properties

        public bool IsBusy { get; private set; }
        public int Count => _queueList.Count;
        public IQueueable[] Items => _queueList.ToArray();

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ItemEnqueued;
        public event EventHandler ItemDequeued;
        public event EventHandler QueueProcessorStarted;
        public event EventHandler QueueProcessorStopped;

        #endregion

        #region Constructors

        public QueueProcessor(
            int maxParallelDownloads,
            PropertyChangedEventHandler propertyChangedEventHandler,
            EventHandler queueProcessorStarted,
            EventHandler queueProcessorStopped,
            EventHandler itemEnqueuedEventHandler,
            EventHandler itemDequeuedEventHandler)
        {
            _queueList = new();
            _processedItems = new();
            _queueListLock = _queueList;
            _processedItemsLock = _processedItems;
            _semaphore = new SemaphoreSlim(maxParallelDownloads);

            IsBusy = false;

            PropertyChanged += propertyChangedEventHandler;
            QueueProcessorStarted += queueProcessorStarted;
            QueueProcessorStopped += queueProcessorStopped;
            ItemEnqueued += itemEnqueuedEventHandler;
            ItemDequeued += itemDequeuedEventHandler;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Add and enqueue <paramref name="items"/> to this <see cref="QueueProcessor"/>.
        /// </summary>
        /// <param name="items">The items to add and enqueue.</param>
        public void Enqueue(params IQueueable[] items)
        {
            bool itemsAdded = false;

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (!item.IsCompleted && !_queueList.Contains(item))
                {
                    _queueList.Add(item);
                    itemsAdded = true;
                }
            }

            if (itemsAdded)
            {
                RaiseEvent(ItemEnqueued);
            }
        }

        /// <summary>
        /// Dequeue and remove <paramref name="items"/> from this <see cref="QueueProcessor"/>.
        /// </summary>
        /// <param name="items">The items to dequeue and remove.</param>
        public void Dequeue(params IQueueable[] items)
        {
            bool itemsRemoved = false;

            foreach (var item in items)
            {
                if (_queueList.Remove(item))
                {
                    itemsRemoved = true;
                }
            }

            if (itemsRemoved)
            {
                RaiseEvent(ItemDequeued);
            }
        }

        /// <summary>
        /// Starts this <see cref="QueueProcessor"/> unless it is already running.
        /// </summary>
        /// <returns>A task that represents the successful completion of all 
        /// enqueued items.</returns>
        public async Task StartAsync()
        {
            bool cancellationRequested;

            if (IsBusy)
            {
                return;
            };

            _tcs = new();
            _cts = new();

            var ct = _cts.Token;

            IsBusy = true;
            RaisePropertyChanged(nameof(IsBusy));
            RaiseEvent(QueueProcessorStarted);

            try
            {
                await Task.Run(async () =>
                {
                    List<Task> tasks = new();

                    foreach (var item in _queueList)
                    {
                        if (item.IsCompleted)
                        {
                            continue;
                        }

                        var t = Task.Run(async () =>
                        {
                            var semTask = _semaphore.WaitAsync(ct);

                            try
                            {
                                await semTask;

                                ct.ThrowIfCancellationRequested();

                                // ensure the item is still enqueued before commencing download
                                if (!_queueList.Contains(item))
                                {
                                    return;
                                }

                                await item.StartAsync();

                                if (item.IsCompleted || item.IsErrored)
                                {
                                    // item processed

                                    Monitor.Enter(_processedItemsLock);
                                    _processedItems.Add(item);
                                    Monitor.Exit(_processedItemsLock);
                                }
                            }
                            finally
                            {
                                if (semTask.IsCompletedSuccessfully)
                                {
                                    _semaphore.Release();
                                }
                            }
                        }, ct);

                        tasks.Add(t);
                    }

                    await Task.WhenAll(tasks);
                });
            }
            catch (OperationCanceledException ex)
            {
                Log.Debug(ex.Message, ex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }

            // remove processed items from the queue list
            foreach (var item in _processedItems)
            {
                _queueList.Remove(item);
            }
            _processedItems.Clear();

            // must be set before auto restarting the queue;
            // also, must be set before setting _tcs due to a race
            // condition when canceling and restarting the queue
            IsBusy = false;

            cancellationRequested = ct.IsCancellationRequested;

            _tcs.SetResult();
            _cts.Dispose();

            // keep running the queue recursively until there are
            // no more queued items or cancellation is requested
            if (_queueList.Count > 0 && !cancellationRequested)
            {
                await StartAsync();
            }

            RaisePropertyChanged(nameof(IsBusy));
            RaiseEvent(QueueProcessorStopped);
        }

        /// <summary>
        /// Starts (or restarts) this <see cref="QueueProcessor"/> with the 
        /// specified <paramref name="items"/> at the top.
        /// </summary>
        /// <param name="items">The items to put at the top of the queue.</param>
        /// <returns>A task that represents the successful completion of all 
        /// enqueued items.</returns>
        public async Task StartWithAsync(IEnumerable<IQueueable> items)
        {
            List<IQueueable> temp = new();

            temp.AddRange(items.Where(o => IsQueued(o)));

            if (temp.Count > 0)
            {
                if (IsBusy)
                {
                    await StopAsync();
                }

                temp.AddRange(_queueList.Where(o => !temp.Contains(o)));

                _queueList.Clear();
                _queueList.AddRange(temp);
            }

            await StartAsync();
        }

        public void Stop()
        {
            if (!IsBusy)
            {
                return;
            };

            _cts?.Cancel();

            // pause items being downloaded
            try
            {
                Parallel.ForEach(_queueList, (item) => item.Pause());
            }
            catch
            {

            }
        }

        public async Task StopAsync()
        {
            Stop();
            await _tcs.Task;
        }

        /// <summary>
        /// Determines if the <paramref name="item"/> is enqueued.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns><see langword="true"/> if the <paramref name="item"/> is enqueued.</returns>
        public bool IsQueued(IQueueable item)
        {
            return _queueList.Contains(item);
        }

        #endregion

        #region Private methods

        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion
    }
}