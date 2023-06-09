﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace AMDownloader.QueueProcessing
{
    internal class QueueProcessor : INotifyPropertyChanged, IEnumerable
    {
        #region Fields

        private readonly SemaphoreSlim _semaphore;
        private readonly object _lockQueueList;
        private readonly List<IQueueable> _queueList;
        private CancellationTokenSource _cts;

        #endregion

        #region Properties

        public bool IsBusy { get; private set; }
        public int Count => _queueList.Count;

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
            _lockQueueList = _queueList;
            _cts = null;

            IsBusy = false;

            PropertyChanged += propertyChangedEventHandler;
            QueueProcessorStarted += queueProcessorStarted;
            QueueProcessorStopped += queueProcessorStopped;
            ItemEnqueued += itemEnqueuedEventHandler;
            ItemDequeued += itemDequeuedEventHandler;
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

        #region Public methods

        /// <summary>
        /// Adds items to the QueueProcessor.
        /// </summary>
        /// <param name="items">The items to add.</param>
        public void Add(params IQueueable[] items)
        {
            bool itemsAdded = false;

            foreach (var item in items)
            {
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
        /// Removes items from the QueueProcessor.
        /// </summary>
        /// <param name="items">The items to remove.</param>
        public void Remove(params IQueueable[] items)
        {
            bool itemsRemoved = false;

            foreach (var item in items)
            {
                if (_queueList.Contains(item))
                {
                    _queueList.Remove(item);
                    itemsRemoved = true;
                }
            }

            if (itemsRemoved)
            {
                RaiseEvent(ItemDequeued);
            }
        }

        public async Task StartAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            RaisePropertyChanged(nameof(IsBusy));
            RaiseEvent(QueueProcessorStarted);

            _cts = new();
            var ct = _cts.Token;

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

                            await item.StartAsync();

                            if (item.IsCompleted)
                            {
                                Monitor.Enter(_lockQueueList);
                                _queueList.Remove(item);
                                Monitor.Exit(_lockQueueList);
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

            _cts.Dispose();

            IsBusy = false;
            RaisePropertyChanged(nameof(IsBusy));
            RaiseEvent(QueueProcessorStopped);
        }

        public void Stop()
        {
            if (!IsBusy) return;

            _cts?.Cancel();

            // pause items being downloaded
            Parallel.ForEach(_queueList, (item) => item.Pause());
        }

        public bool IsQueued(IQueueable value)
        {
            return _queueList.Contains(value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public QueueEnumerator GetEnumerator()
        {
            return new QueueEnumerator(_queueList.ToArray());
        }

        #endregion
    }

    #region Enumerator

    internal class QueueEnumerator : IEnumerator
    {
        readonly IQueueable[] _queueables;
        int position = -1;

        public QueueEnumerator(IQueueable[] queueables)
        {
            _queueables = queueables;
        }

        public bool MoveNext()
        {
            position++;
            return position < _queueables.Length;
        }

        public void Reset()
        {
            position = -1;
        }

        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }

        public IQueueable Current
        {
            get
            {
                try
                {
                    return _queueables[position];
                }
                catch (IndexOutOfRangeException)
                {
                    throw new InvalidOperationException();
                }
            }
        }
    }

    #endregion
}