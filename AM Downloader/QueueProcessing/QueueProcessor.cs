// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections;
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
        private readonly object _queueListLock;
        private readonly List<IQueueable> _queueList;
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
            _semaphore = new SemaphoreSlim(maxParallelDownloads);
            _queueList = new();
            _queueListLock = _queueList;

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
                itemsRemoved = _queueList.Remove(item);
            }

            if (itemsRemoved)
            {
                RaiseEvent(ItemDequeued);
            }
        }

        /// <summary>
        /// Starts this <see cref="QueueProcessor"/> unless it is already running.
        /// </summary>
        /// <returns></returns>
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
                                Monitor.Enter(_queueListLock);
                                _queueList.Remove(item);
                                Monitor.Exit(_queueListLock);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            ct.ThrowIfCancellationRequested();
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

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {

                }
            });

            // must be set before restarting the queue automatically
            IsBusy = false;

            cancellationRequested = ct.IsCancellationRequested;
            _cts.Dispose();
            _tcs.SetResult();

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
        /// <paramref name="items"/> at the top.
        /// </summary>
        /// <param name="items">The items to put at the top of the queue.</param>
        /// <returns></returns>
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
        /// Determines if the <paramref name="value"/> is enqueued.
        /// </summary>
        /// <param name="value">The item to check.</param>
        /// <returns><see langword="true"/> if the <paramref name="value"/> is enqueued.</returns>
        public bool IsQueued(IQueueable value)
        {
            return _queueList.Contains(value);
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