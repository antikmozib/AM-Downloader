// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Common;
using AMDownloader.Helpers;
using AMDownloader.ObjectModel.Serializable;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using AMDownloader.RequestThrottling;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Serialization;

namespace AMDownloader
{
    internal delegate Task AddItemsAsyncDelegate(string destination, bool enqueue, bool start, params string[] urls);

    internal delegate MessageBoxResult DisplayMessageDelegate(
        string message, string title = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.OK);

    internal delegate bool? ShowWindowDelegate(object viewModel);

    internal enum Category
    {
        All, Ready, Queued, Downloading, Paused, Finished, Errored
    }

    internal class DownloaderViewModel : INotifyPropertyChanged, IClosing
    {
        #region Fields

        /// <summary>
        /// Gets the interval between refreshing the CollectionView.
        /// </summary>
        private const int _collectionRefreshDelay = 1000;
        private readonly DisplayMessageDelegate _displayMessage;
        private readonly ClipboardObserver _clipboardService;
        private readonly object _lockDownloadItemsList;
        private readonly object _lockBytesDownloaded;
        private readonly object _lockBytesTransferredOverLifetime;
        private readonly object _lockCtsRefreshViewList;
        private readonly SemaphoreSlim _semaphoreMeasuringSpeed;
        private readonly SemaphoreSlim _semaphoreUpdatingList;
        private readonly SemaphoreSlim _semaphoreRefreshingView;
        private CancellationTokenSource _ctsUpdatingList;
        private readonly List<CancellationTokenSource> _ctsRefreshViewList;
        private RequestThrottler _requestThrottler;
        private readonly HttpClient _client;
        private readonly ShowWindowDelegate _showWindow;
        private bool _resetAllSettingsOnClose;
        private readonly IProgress<long> _progressReporter;

        #endregion

        #region Properties

        public ObservableCollection<DownloaderObjectModel> DownloadItemsList { get; }
        public ObservableCollection<Category> CategoriesList { get; }
        public ICollectionView DownloadItemsView { get; }
        public ICollectionViewLiveShaping CategorisedDownloadItemsView { get; }
        public QueueProcessor QueueProcessor { get; }
        public int Progress { get; private set; }
        public long BytesDownloadedThisSession { get; private set; }
        public long? Speed { get; private set; }
        public int Count { get; private set; }
        public int ReadyCount { get; private set; }
        public int DownloadingCount { get; private set; }
        public int QueuedCount { get; private set; }
        public int FinishedCount { get; private set; }
        public int ErroredCount { get; private set; }
        public int PausedCount { get; private set; }
        public bool IsBackgroundWorking
        {
            get
            {
                if (_ctsUpdatingList == null)
                {
                    return false;
                }
                else
                {
                    try
                    {
                        var ct = _ctsUpdatingList.Token;
                        return true;
                    }
                    catch (Exception ex)
                    when (ex is ObjectDisposedException)
                    {
                        return false;
                    }
                }
            }
        }
        public bool IsDownloading => DownloadingCount > 0;
        public string Status { get; private set; }

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Commands

        public ICommand AddCommand { get; private set; }
        public ICommand StartCommand { get; private set; }
        public ICommand RemoveCommand { private get; set; }
        public ICommand CancelCommand { private get; set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand OpenContainingFolderCommand { get; private set; }
        public ICommand StartQueueCommand { get; private set; }
        public ICommand StopQueueCommand { get; private set; }
        public ICommand CategoryChangedCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand EnqueueCommand { get; private set; }
        public ICommand DequeueCommand { get; private set; }
        public ICommand CopyLinkToClipboardCommand { get; private set; }
        public ICommand ClearFinishedDownloadsCommand { get; private set; }
        public ICommand CancelBackgroundTaskCommand { get; private set; }
        public ICommand CheckForUpdatesCommand { get; private set; }
        public ICommand UIClosedCommand { get; private set; }

        #endregion

        #region Constructors

        public DownloaderViewModel(DisplayMessageDelegate displayMessage, ShowWindowDelegate showWindow)
        {
            DownloadItemsList = new();
            DownloadItemsList.CollectionChanged += DownloadItemsList_CollectionChanged;

            CategoriesList = new ObservableCollection<Category>();

            QueueProcessor = new QueueProcessor(
                Settings.Default.MaxParallelDownloads,
                QueueProcessor_PropertyChanged,
                QueueProcessor_Started,
                QueueProcessor_Stopped,
                QueueProcessor_ItemEnqueued,
                QueueProcessor_ItemDequeued);

            DownloadItemsView = CollectionViewSource.GetDefaultView(DownloadItemsList);
            DownloadItemsView.CurrentChanged += CollectionView_CurrentChanged;

            this.Progress = 0;
            this.BytesDownloadedThisSession = 0;
            this.Speed = null;
            this.Count = 0;
            this.ReadyCount = 0;
            this.DownloadingCount = 0;
            this.QueuedCount = 0;
            this.FinishedCount = 0;
            this.ErroredCount = 0;
            this.PausedCount = 0;
            this.Status = "Ready";
            this._progressReporter = new Progress<long>(value =>
            {
                Monitor.Enter(_lockBytesDownloaded);
                try
                {
                    this.BytesDownloadedThisSession += value;
                }
                finally
                {
                    Monitor.Exit(_lockBytesDownloaded);
                }
            });

            _client = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(Settings.Default.ConnectionTimeout)
            };
            _requestThrottler = new RequestThrottler(Constants.RequestThrottlerInterval);
            _clipboardService = new ClipboardObserver();
            _semaphoreMeasuringSpeed = new SemaphoreSlim(1);
            _semaphoreUpdatingList = new SemaphoreSlim(1);
            _semaphoreRefreshingView = new SemaphoreSlim(1);
            _ctsRefreshViewList = new();
            _displayMessage = displayMessage;
            _lockDownloadItemsList = DownloadItemsList;
            _lockBytesDownloaded = this.BytesDownloadedThisSession;
            _lockBytesTransferredOverLifetime = Settings.Default.BytesTransferredOverLifetime;
            _lockCtsRefreshViewList = _ctsRefreshViewList;
            _showWindow = showWindow;
            _resetAllSettingsOnClose = false;

            AddCommand = new RelayCommand<object>(
                Add, Add_CanExecute);
            StartCommand = new RelayCommand<object>(
                Start, Start_CanExecute);
            RemoveCommand = new RelayCommand<object>(
                Remove, Remove_CanExecute);
            CancelCommand = new RelayCommand<object>(
                Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand<object>(
                Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand<object>(
                Open, Open_CanExecute);
            OpenContainingFolderCommand = new RelayCommand<object>(
                OpenContainingFolder, OpenContainingFolder_CanExecute);
            StartQueueCommand = new RelayCommand<object>(
                StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand<object>(
                StopQueue, StopQueue_CanExecute);
            CategoryChangedCommand = new RelayCommand<object>(CategoryChanged);
            OptionsCommand = new RelayCommand<object>(ShowOptions);
            EnqueueCommand = new RelayCommand<object>(
                Enqueue, Enqueue_CanExecute);
            DequeueCommand = new RelayCommand<object>(
                Dequeue, Dequeue_CanExecute);
            CopyLinkToClipboardCommand = new RelayCommand<object>(
                CopyLinkToClipboard, CopyLinkToClipboardCommand_CanExecute);
            ClearFinishedDownloadsCommand = new RelayCommand<object>(
                ClearFinishedDownloads, ClearFinishedDownloads_CanExecute);
            CancelBackgroundTaskCommand = new RelayCommand<object>(
                CancelBackgroundTask, CancelBackgroundTask_CanExecute);
            CheckForUpdatesCommand = new RelayCommand<object>(CheckForUpdates);
            UIClosedCommand = new RelayCommand<object>(UIClosed);

            foreach (Category cat in (Category[])Enum.GetValues(typeof(Category)))
            {
                CategoriesList.Add(cat);
            }

            // Load last selected category
            if (string.IsNullOrEmpty(Settings.Default.LastSelectedCatagory))
            {
                SwitchCategory(Category.All);
            }
            else
            {
                SwitchCategory((Category)Enum.Parse(typeof(Category), Settings.Default.LastSelectedCatagory));
            }

            // Check for updates
            if (Settings.Default.AutoCheckForUpdates)
            {
                Task.Run(async () => await TriggerUpdateCheckAsync(true));
            }

            // Populate history
            if (File.Exists(Paths.DownloadsHistoryFile))
            {
                _ctsUpdatingList = new CancellationTokenSource();
                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                var ct = _ctsUpdatingList.Token;

                Task.Run(async () =>
                {
                    await _semaphoreUpdatingList.WaitAsync();

                    try
                    {
                        this.Status = "Restoring data...";
                        RaisePropertyChanged(nameof(this.Status));

                        SerializableDownloaderObjectModelList source;
                        XmlSerializer xmlReader = new(typeof(SerializableDownloaderObjectModelList));

                        using (var streamReader = new StreamReader(Paths.DownloadsHistoryFile))
                        {
                            source = (SerializableDownloaderObjectModelList)xmlReader.Deserialize(streamReader);
                        }

                        var sourceObjects = source.Objects.ToArray();
                        var itemsToAdd = new DownloaderObjectModel[sourceObjects.Length];
                        var itemsToEnqueue = new List<IQueueable>();
                        var total = sourceObjects.Length;

                        for (int i = 0; i < sourceObjects.Length; i++)
                        {
                            if (ct.IsCancellationRequested) break;
                            if (sourceObjects[i] == null) continue;

                            int progress = (int)((double)(i + 1) / total * 100);
                            this.Progress = progress;
                            this.Status = "Restoring " + (i + 1) + " of " + total + ": " + sourceObjects[i].Url;
                            RaisePropertyChanged(nameof(this.Progress));
                            RaisePropertyChanged(nameof(this.Status));

                            var item = new DownloaderObjectModel(
                                _client,
                                sourceObjects[i].Url,
                                sourceObjects[i].Destination,
                                sourceObjects[i].DateCreated,
                                sourceObjects[i].TotalBytesToDownload,
                                sourceObjects[i].StatusCode,
                                sourceObjects[i].Status,
                                Download_Created,
                                Download_Started,
                                Download_Stopped,
                                Download_PropertyChanged,
                                _progressReporter);

                            if (sourceObjects[i].IsQueued)
                            {
                                itemsToEnqueue.Add(item);
                            }

                            itemsToAdd[i] = item;
                        }

                        this.Status = "Refreshing...";
                        RaisePropertyChanged(nameof(this.Status));

                        AddObjects(itemsToAdd);
                        QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    finally
                    {
                        _semaphoreUpdatingList.Release();
                    }

                    this.Progress = 0;
                    this.Status = "Ready";
                    RaisePropertyChanged(nameof(this.Progress));
                    RaisePropertyChanged(nameof(this.Status));
                }).ContinueWith(t =>
                {
                    _ctsUpdatingList.Dispose();
                    RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                    RefreshCollectionView();
                });
            }
        }

        #endregion

        #region Command methods

        private void CategoryChanged(object obj)
        {
            if (obj == null)
            {
                return;
            }

            SwitchCategory((Category)obj);
        }

        private void Start(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            Task.Run(async () => await StartDownloadAsync(false, items.ToArray()));
        }

        private bool Start_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (!items.Any())
            {
                return false;
            }

            return (from item
                    in items
                    where !item.IsDownloading && !item.IsCompleted
                    select item).Count() == items.Count();
        }

        private void Pause(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            foreach (var item in items)
            {
                QueueProcessor.Dequeue(item);
                item.Pause();
            }
        }

        private bool Pause_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (!items.Any())
            {
                return false;
            }

            return (from item
                    in items
                    where item.IsDownloading && item.SupportsResume
                    select item).Count() == items.Count();
        }

        private void Cancel(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            foreach (var item in items)
            {
                QueueProcessor.Dequeue(item);
                item.Cancel();
            }

            RefreshCollectionView();
        }

        private bool Cancel_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (!items.Any())
            {
                return false;
            }

            return (from item
                    in items
                    where item.IsDownloading || item.IsPaused || (item.IsErrored && item.SupportsResume)
                    select item).Count() == items.Count();
        }

        private void Add(object obj)
        {
            AddDownloadViewModel vm = new(_showWindow);
            DownloaderObjectModel[] itemsAdded = null;

            if (_showWindow.Invoke(vm) == true)
            {
                _ctsUpdatingList = new CancellationTokenSource();
                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                var ct = _ctsUpdatingList.Token;

                Task.Run(async () =>
                {
                    itemsAdded = await AddItemsAsync(vm.GeneratedUrls, vm.SaveToFolder, vm.Enqueue, ct);
                }
                ).ContinueWith(async t =>
                {
                    _ctsUpdatingList.Dispose();
                    RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                    RefreshCollectionView();

                    if (vm.StartDownload)
                    {
                        await StartDownloadAsync(vm.Enqueue, itemsAdded);
                    }
                });
            }
        }

        private bool Add_CanExecute(object obj)
        {
            return !this.IsBackgroundWorking;
        }

        private void Remove(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();
            var itemsDeleteable = from item in items where item.IsCompleted select item;
            var delete = true;

            if (itemsDeleteable.Any())
            {
                var result = _displayMessage.Invoke(
                    "Also delete the files from storage?",
                    "Remove",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Asterisk,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.No)
                {
                    delete = false;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            _ctsUpdatingList = new CancellationTokenSource();
            RaisePropertyChanged(nameof(IsBackgroundWorking));
            var ct = _ctsUpdatingList.Token;

            Task.Run(async () => await RemoveObjectsAsync(items, delete, ct)).ContinueWith(t =>
            {
                _ctsUpdatingList.Dispose();
                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                RefreshCollectionView();
            });
        }

        private bool Remove_CanExecute(object obj)
        {
            if (obj == null || this.IsBackgroundWorking)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            return items.Any();
        }

        private void Open(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (items.Count() > 1)
            {
                var result = _displayMessage.Invoke(
                    "Opening too many files at the same file may cause the system to crash.\n\nProceed anyway?",
                    $"Open {items.Count()} Files",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            foreach (var item in items)
            {
                Process.Start("explorer.exe", "\"" + item.Destination + "\"");
            }
        }

        private bool Open_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (!items.Any())
            {
                return false;
            }

            return (from item
                    in items
                    where item.IsCompleted
                    select item).Count() == items.Count();
        }

        private void OpenContainingFolder(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();
            var itemsOpenable = from item in items
                                where File.Exists(item.Destination) ||
                                Directory.Exists(Path.GetDirectoryName(item.Destination))
                                select item;

            if (itemsOpenable.Count() > 1)
            {
                var result = _displayMessage.Invoke(
                    "Opening too many folders at the same time may cause the system to crash.\n\nProceed anyway?",
                    $"Open {items.Count()} Folders",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            foreach (var item in itemsOpenable)
            {
                if (File.Exists(item.Destination))
                {
                    Process.Start("explorer.exe", "/select, \"\"" + item.Destination + "\"\"");
                }
                else if (Directory.Exists(Path.GetDirectoryName(item.Destination)))
                {
                    Process.Start("explorer.exe", Path.GetDirectoryName(item.Destination));
                }
            }
        }

        private bool OpenContainingFolder_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            return items.Any();
        }

        private void StartQueue(object obj)
        {
            Task.Run(async () =>
            {
                await QueueProcessor.StartAsync();
            });
        }

        private bool StartQueue_CanExecute(object obj)
        {
            return !QueueProcessor.IsBusy && QueueProcessor.Count > 0;
        }

        private void StopQueue(object obj)
        {
            QueueProcessor.Stop();
        }

        private bool StopQueue_CanExecute(object obj)
        {
            return QueueProcessor.IsBusy;
        }

        private void ShowOptions(object obj)
        {
            var vm = new OptionsViewModel();

            if (_showWindow.Invoke(vm) == true)
            {
                if (vm.ResetSettingsOnClose)
                {
                    _resetAllSettingsOnClose = true;
                }
            }
        }

        private void Enqueue(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            foreach (var item in items)
            {
                QueueProcessor.Enqueue(item);
            }
        }

        private bool Enqueue_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (!items.Any())
            {
                return false;
            }

            return (from item
                    in items
                    where !item.IsDownloading && !item.IsCompleted && !QueueProcessor.IsQueued(item)
                    select item).Count() == items.Count();
        }

        private void Dequeue(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            QueueProcessor.Dequeue(items.ToArray());
        }

        private bool Dequeue_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            if (!items.Any())
            {
                return false;
            }

            return (from item
                    in items
                    where !item.IsDownloading && QueueProcessor.IsQueued(item)
                    select item).Count() == items.Count();
        }

        private void CopyLinkToClipboard(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();
            var clipText = string.Empty;
            var counter = 0;
            var total = items.Count();

            foreach (var item in items)
            {
                clipText += item.Url;
                if (counter < total - 1)
                {
                    clipText += '\n';
                }
                counter++;
            }

            _clipboardService.SetText(clipText);
        }

        private bool CopyLinkToClipboardCommand_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();

            return items.Any();
        }

        private void ClearFinishedDownloads(object obj)
        {
            _ctsUpdatingList = new CancellationTokenSource();
            RaisePropertyChanged(nameof(this.IsBackgroundWorking));

            var ct = _ctsUpdatingList.Token;

            var items = (from item in DownloadItemsList where item.IsCompleted select item);

            Task.Run(async () => await RemoveObjectsAsync(items, false, ct)).ContinueWith(t =>
            {
                _ctsUpdatingList.Dispose();
                RaisePropertyChanged(nameof(this.IsBackgroundWorking));

                RefreshCollectionView();
            });
        }

        private bool ClearFinishedDownloads_CanExecute(object obj)
        {
            if (this.IsBackgroundWorking)
            {
                return false;
            }

            return this.FinishedCount > 0;
        }

        private void CancelBackgroundTask(object obj)
        {
            try
            {
                _ctsUpdatingList?.Cancel();
            }
            catch (ObjectDisposedException)
            {

            }
        }

        private bool CancelBackgroundTask_CanExecute(object obj)
        {
            return this.IsBackgroundWorking;
        }

        private void CheckForUpdates(object obj)
        {
            Task.Run(async () => await TriggerUpdateCheckAsync());
        }

        private void UIClosed(object obj)
        {
            if (_resetAllSettingsOnClose)
            {
                Functions.ResetAllSettings();
            }
        }

        #endregion

        #region Public methods

        public bool OnClosing()
        {
            if (IsBackgroundWorking)
            {
                if (_displayMessage.Invoke(
                    "Background operation in progress. Cancel and exit program?", "Exit",
                    MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.No)
                {
                    return false;
                }

                _ctsUpdatingList.Cancel();
            }

            this.Status = "Saving data...";
            RaisePropertyChanged(nameof(this.Status));

            Task<bool> closingTask = Task.Run(async () =>
            {
                if (QueueProcessor.IsBusy)
                {
                    await QueueProcessor.StopAsync();
                }

                await _semaphoreUpdatingList.WaitAsync();

                try
                {
                    var writer = new XmlSerializer(typeof(SerializableDownloaderObjectModelList));
                    var list = new SerializableDownloaderObjectModelList();
                    var index = 0;

                    Directory.CreateDirectory(Paths.LocalAppDataFolder);

                    foreach (var item in DownloadItemsList)
                    {
                        if (item.IsDownloading)
                        {
                            await item.PauseAsync();
                        }
                        else if (item.IsCompleted && Settings.Default.ClearFinishedDownloadsOnExit)
                        {
                            continue;
                        }

                        var sItem = new SerializableDownloaderObjectModel
                        {
                            Index = index++,
                            Url = item.Url,
                            Destination = item.Destination,
                            TotalBytesToDownload = item.TotalBytesToDownload,
                            IsQueued = QueueProcessor.IsQueued(item),
                            Status = item.Status,
                            DateCreated = item.DateCreated,
                            StatusCode = item.StatusCode
                        };

                        list.Objects.Add(sItem);
                    }

                    using var streamWriter = new StreamWriter(Paths.DownloadsHistoryFile, false);
                    writer.Serialize(streamWriter, list);
                }
                catch
                {
                    // close even when an exception occurs
                }
                finally
                {
                    _semaphoreUpdatingList.Release();
                }

                Settings.Default.Save();
                return true;
            });

            return closingTask.GetAwaiter().GetResult();
        }

        #endregion

        #region Private methods

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private void SwitchCategory(Category category)
        {
            switch (category)
            {
                case Category.All:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        return true;
                    });
                    break;

                case Category.Downloading:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.IsDownloading;
                    });
                    break;

                case Category.Finished:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.IsCompleted;
                    });
                    break;

                case Category.Paused:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.IsPaused;
                    });
                    break;

                case Category.Queued:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return QueueProcessor.IsQueued(item);
                    });
                    break;

                case Category.Ready:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.IsReady && !QueueProcessor.IsQueued(item);
                    });
                    break;

                case Category.Errored:
                    DownloadItemsView.Filter = new Predicate<object>((o) =>
                    {
                        var item = o as DownloaderObjectModel;
                        return item.IsErrored;
                    });
                    break;
            }

            Settings.Default.LastSelectedCatagory = category.ToString();
        }

        private void RefreshCollectionView()
        {
            if (_semaphoreUpdatingList.CurrentCount == 0)
            {
                return;
            }

            // cancel all pending refreshes
            Monitor.Enter(_lockCtsRefreshViewList);
            foreach (var oldCts in _ctsRefreshViewList)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            _ctsRefreshViewList.Clear();
            var newCts = new CancellationTokenSource();
            _ctsRefreshViewList.Add(newCts);
            Monitor.Exit(_lockCtsRefreshViewList);

            Task.Run(async () =>
            {
                var semTask = _semaphoreRefreshingView.WaitAsync(newCts.Token);
                var throttle = Task.Delay(_collectionRefreshDelay);
                try
                {
                    await semTask;
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        DownloadItemsView.Refresh();
                        //CommandManager.InvalidateRequerySuggested();
                    });
                    await throttle;
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    if (semTask.IsCompletedSuccessfully)
                    {
                        _semaphoreRefreshingView.Release();
                    }
                }
            }, newCts.Token);
        }

        private async Task<DownloaderObjectModel[]> AddItemsAsync(IEnumerable<string> urls, string destination, bool enqueue, CancellationToken ct)
        {
            var existingUrls = from di in DownloadItemsList select di.Url;
            var existingDestinations = from di in DownloadItemsList select di.Destination;
            var itemsToAdd = new List<DownloaderObjectModel>();
            var itemsToEnqueue = new List<IQueueable>();
            var itemsAdded = new List<DownloaderObjectModel>();
            var itemsExist = new List<string>();
            var itemsErrored = new List<string>();
            var wasCanceled = false;
            var totalItems = urls.Count();
            var counter = 0;

            await _semaphoreUpdatingList.WaitAsync();

            foreach (var url in urls)
            {
                string fileName, filePath;

                counter++;

                this.Progress = (int)((double)counter / totalItems * 100);
                this.Status = $"Creating download {counter} of {totalItems}: {url}";
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.Progress));

                fileName = Functions.GetFileNameFromUrl(url);

                if (string.IsNullOrEmpty(fileName))
                {
                    itemsErrored.Add(url);
                    continue;
                }

                filePath = Functions.GetNewFileName(destination + Functions.GetFileNameFromUrl(url));

                if (existingUrls.Contains(url) || existingDestinations.Contains(filePath))
                {
                    itemsExist.Add(url);
                    continue;
                }

                DownloaderObjectModel item;
                item = new DownloaderObjectModel(
                        _client,
                        url,
                        filePath,
                        Download_Created,
                        Download_Started,
                        Download_Stopped,
                        Download_PropertyChanged,
                        _progressReporter);

                itemsToAdd.Add(item);

                if (enqueue)
                {
                    // enqueue lazily because we might cancel the process
                    // before the items appear in the downloads list
                    itemsToEnqueue.Add(item);
                }

                if (ct.IsCancellationRequested)
                {
                    wasCanceled = true;
                    break;
                }
            }

            if (!wasCanceled)
            {
                this.Status = "Refreshing...";
                RaisePropertyChanged(nameof(this.Status));

                AddObjects(itemsToAdd.ToArray());
                QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                itemsAdded.AddRange(itemsToAdd);
            }

            this.Progress = 0;
            this.Status = "Ready";
            RaisePropertyChanged(nameof(this.Progress));
            RaisePropertyChanged(nameof(this.Status));

            _semaphoreUpdatingList.Release();

            if (!wasCanceled)
            {
                if (itemsExist.Count > 0)
                {
                    _showWindow.Invoke(new ListViewerViewModel(itemsExist,
                        "The following URLs were not added because they are already in the list:",
                        "Duplicate Entries"));
                }

                if (itemsErrored.Count > 0)
                {
                    _showWindow.Invoke(new ListViewerViewModel(itemsErrored,
                        "The following URLs were not added because they are invalid:",
                        "Invalid Entries"));
                }
            }

            return itemsAdded.ToArray();
        }

        private void AddObjects(params DownloaderObjectModel[] objects)
        {
            Monitor.Enter(_lockDownloadItemsList);
            int total = objects.Length;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        if (objects[i] == null) continue;

                        DownloadItemsList.Add(objects[i]);
                    }
                });
            }
            catch
            {
            }
            finally
            {
                Monitor.Exit(_lockDownloadItemsList);
            }
        }

        private async Task RemoveObjectsAsync(IEnumerable<DownloaderObjectModel> items, bool delete, CancellationToken ct)
        {
            List<DownloaderObjectModel> itemsToRemove = new();
            List<IQueueable> itemsToDequeue = new();
            List<string> failed = new();
            int counter = 0;
            int total = items.Count();
            string primaryStatus = delete ? "Deleting" : "Removing";

            await _semaphoreUpdatingList.WaitAsync();

            foreach (var item in items)
            {
                counter++;

                this.Status = $"{primaryStatus} {counter} of {total}: {item.Name}";
                this.Progress = (int)((double)counter / total * 100);
                RaisePropertyChanged(nameof(this.Status));
                RaisePropertyChanged(nameof(this.Progress));

                var queued = QueueProcessor.IsQueued(item);

                if (item.IsDownloading && queued)
                {
                    await QueueProcessor.StopAsync();
                }

                if (!item.IsCompleted && !item.IsReady)
                {
                    await item.CancelAsync();

                    if (File.Exists(item.TempDestination))
                    {
                        failed.Add(item.TempDestination);
                    }
                }
                else if (item.IsCompleted && delete)
                {
                    try
                    {
                        FileSystem.DeleteFile(
                            item.Destination,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);

                        if (File.Exists(item.Destination))
                        {
                            failed.Add(item.Destination);
                        }
                    }
                    catch
                    {
                        failed.Add(item.Destination);
                    }
                }

                if (queued)
                {
                    itemsToDequeue.Add(item);
                }

                itemsToRemove.Add(item);

                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            this.Status = "Refreshing...";
            RaisePropertyChanged(nameof(this.Status));

            Application.Current.Dispatcher.Invoke(() =>
            {
                Monitor.Enter(_lockDownloadItemsList);
                if (itemsToRemove.Count == DownloadItemsList.Count)
                {
                    DownloadItemsList.Clear();
                }
                else
                {
                    for (int i = 0; i < itemsToRemove.Count; i++)
                    {
                        DownloadItemsList.Remove(itemsToRemove[i]);
                    }
                }
                Monitor.Exit(_lockDownloadItemsList);
            });

            QueueProcessor.Dequeue(itemsToDequeue.ToArray());

            this.Progress = 0;
            this.Status = "Ready";
            RaisePropertyChanged(nameof(this.Progress));
            RaisePropertyChanged(nameof(this.Status));

            _semaphoreUpdatingList.Release();

            if (failed.Count > 0)
            {
                string title, description;

                if (delete)
                {
                    title = "Deletion Errors";
                    description = "The following files were not deleted due to errors:";
                }
                else
                {
                    title = "Removal Errors";
                    description = "The following files were not removed due to errors:";
                }

                _showWindow(new ListViewerViewModel(failed, title, description));
            }
        }

        /// <summary>
        /// Starts downloading the specified items. If the number of items is larger than the
        /// maximum number of parallel downloads allowed, the items are enqueued instead.
        /// </summary>
        /// <param name="enqueue">Whether to enqueue the items instead of immediately starting the download.</param>
        /// <param name="items">The items to download.</param>
        /// <returns></returns>
        private async Task StartDownloadAsync(bool enqueue, params DownloaderObjectModel[] items)
        {
            bool forceEnqueue = enqueue;
            List<IQueueable> itemsToEnqueue = new();
            List<Task> itemsToDownload = new();

            if (items.Length > Settings.Default.MaxParallelDownloads)
            {
                forceEnqueue = true;
            }

            foreach (var item in items)
            {
                if (forceEnqueue)
                {
                    itemsToEnqueue.Add(item);
                }
                else
                {
                    QueueProcessor.Dequeue(item);
                    itemsToDownload.Add(Task.Run(async () => await item.StartAsync()));
                }
            }

            if (forceEnqueue)
            {
                QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                await QueueProcessor.StartWithAsync(itemsToEnqueue);
            }
            else
            {
                await Task.WhenAll(itemsToDownload);
            }
        }

        private void StartReportingSpeed()
        {
            if (_semaphoreMeasuringSpeed.CurrentCount == 0) return;

            Task.Run(async () =>
            {
                await _semaphoreMeasuringSpeed.WaitAsync();
                var stopWatch = new Stopwatch();
                long bytesFrom;
                long bytesTo;
                long bytesCaptured;
                do
                {
                    stopWatch.Restart();
                    bytesFrom = this.BytesDownloadedThisSession;
                    await Task.Delay(1000);
                    bytesTo = this.BytesDownloadedThisSession;
                    stopWatch.Stop();
                    bytesCaptured = bytesTo - bytesFrom;

                    if (bytesCaptured >= 0)
                    {
                        this.Speed = (long)(bytesCaptured / ((double)stopWatch.ElapsedMilliseconds / 1000));
                        RaisePropertyChanged(nameof(this.Speed));
                        RaisePropertyChanged(nameof(this.BytesDownloadedThisSession));
                    }
                } while (this.DownloadingCount > 0);

                this.Speed = null;
                RaisePropertyChanged(nameof(this.Speed));
                _semaphoreMeasuringSpeed.Release();
            });
        }

        private async Task TriggerUpdateCheckAsync(bool silent = false)
        {
            string url = await AppUpdateService.GetUpdateUrl(
                Constants.UpdateServer,
                Assembly.GetExecutingAssembly().GetName().Name,
                Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                _client);

            if (string.IsNullOrEmpty(url))
            {
                if (!silent)
                {
                    _displayMessage.Invoke(
                        "No new updates are available.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (_displayMessage.Invoke(
                "An update is available.\n\nWould you like to download it now?", "Update",
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes) == MessageBoxResult.Yes)
            {
                Process.Start("explorer.exe", url);
            }
        }

        #endregion

        #region Event handlers

        private void QueueProcessor_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void QueueProcessor_Stopped(object sender, EventArgs e)
        {
        }

        private void QueueProcessor_Started(object sender, EventArgs e)
        {
        }

        private void QueueProcessor_ItemEnqueued(object sender, EventArgs e)
        {
            RefreshCollectionView();
        }

        private void QueueProcessor_ItemDequeued(object sender, EventArgs e)
        {
            RefreshCollectionView();
        }

        private void Download_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        private void Download_Created(object sender, EventArgs e)
        {
            RefreshCollectionView();
        }

        private void Download_Started(object sender, EventArgs e)
        {
            RefreshCollectionView();
            StartReportingSpeed();
        }

        private void Download_Stopped(object sender, EventArgs e)
        {
            if (this.DownloadingCount == 0)
            {
                this.Status = "Ready";
                RaisePropertyChanged(nameof(this.Status));
            }

            RefreshCollectionView();

            Monitor.Enter(_lockBytesTransferredOverLifetime);
            try
            {
                Settings.Default.BytesTransferredOverLifetime +=
                    (ulong)(sender as DownloaderObjectModel).BytesDownloadedThisSession;
            }
            finally
            {
                Monitor.Exit(_lockBytesTransferredOverLifetime);
            }
        }

        private void DownloadItemsList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
        }

        private void CollectionView_CurrentChanged(object sender, EventArgs e)
        {
            this.Count = DownloadItemsList.Count;
            this.QueuedCount = QueueProcessor.Count;
            this.ReadyCount = DownloadItemsList.Count(o => o.IsReady && !QueueProcessor.IsQueued(o));
            this.DownloadingCount = DownloadItemsList.Count(o => o.IsDownloading);
            this.PausedCount = DownloadItemsList.Count(o => o.IsPaused);
            this.FinishedCount = DownloadItemsList.Count(o => o.IsCompleted);
            this.ErroredCount = DownloadItemsList.Count(o => o.IsErrored);

            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.DownloadingCount));
            RaisePropertyChanged(nameof(this.ErroredCount));
            RaisePropertyChanged(nameof(this.FinishedCount));
            RaisePropertyChanged(nameof(this.PausedCount));
            RaisePropertyChanged(nameof(this.QueuedCount));
            RaisePropertyChanged(nameof(this.ReadyCount));
            RaisePropertyChanged(nameof(this.IsDownloading));

            if (!this.IsBackgroundWorking)
            {
                if (this.DownloadingCount > 0)
                {
                    this.Status = $"{this.DownloadingCount} item{(this.DownloadingCount > 1 ? "s" : "")} downloading";
                }
                else
                {
                    if (_semaphoreUpdatingList.CurrentCount > 0)
                    {
                        this.Status = "Ready";
                    }
                }

                RaisePropertyChanged(nameof(this.Status));
            }
        }

        #endregion    
    }
}