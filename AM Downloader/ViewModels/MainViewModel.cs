// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.Models.Serializable;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace AMDownloader.ViewModels
{
    internal delegate Task AddItemsAsyncDelegate(string destination, bool enqueue, bool start, params string[] urls);

    internal delegate bool? ShowWindowDelegate(object viewModel);

    internal delegate bool? ShowPromptDelegate(
        string promptText,
        string caption,
        PromptButton button,
        PromptIcon icon,
        bool defaultResult = true);

    internal enum Category
    {
        All, Ready, Queued, Downloading, Paused, Finished, Errored
    }

    internal class MainViewModel : INotifyPropertyChanged, IClosing
    {
        #region Fields

        private readonly HttpClient _client;
        private readonly IProgress<long> _progressReporter;
        private readonly ClipboardObserver _clipboardService;
        private readonly SemaphoreSlim _semaphoreUpdatingList;
        private readonly SemaphoreSlim _semaphoreRefreshingView;
        private readonly SemaphoreSlim _semaphoreMeasuringSpeed;
        private readonly ShowWindowDelegate _showWindow;
        private readonly ShowPromptDelegate _showPrompt;
        private CancellationTokenSource _ctsUpdatingList;
        private readonly List<CancellationTokenSource> _ctsRefreshViewList;
        private readonly object _ctsRefreshViewListLock;
        private readonly object _downloadItemsCollectionLock;
        private readonly object _bytesDownloadedLock;
        private readonly object _bytesTransferredOverLifetimeLock;
        private bool _resetAllSettingsOnClose;

        #endregion

        #region Properties

        public ObservableCollection<DownloaderObjectModel> DownloadItemsCollection { get; }
        public ObservableCollection<Category> CategoriesCollection { get; }
        public ICollectionView DownloadItemsView { get; }
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
                // null check necessary as it's always null at launch
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
                    catch (ObjectDisposedException)
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
        public ICommand SettingsCommand { get; private set; }
        public ICommand EnqueueCommand { get; private set; }
        public ICommand DequeueCommand { get; private set; }
        public ICommand CopyLinkToClipboardCommand { get; private set; }
        public ICommand ClearFinishedDownloadsCommand { get; private set; }
        public ICommand CancelBackgroundTaskCommand { get; private set; }
        public ICommand CheckForUpdatesCommand { get; private set; }
        public ICommand UIClosedCommand { get; private set; }

        #endregion

        #region Constructors

        public MainViewModel(ShowPromptDelegate showPrompt, ShowWindowDelegate showWindow)
        {
            DownloadItemsCollection = new();

            CategoriesCollection = new ObservableCollection<Category>();

            QueueProcessor = new QueueProcessor(
                Settings.Default.MaxParallelDownloads,
                QueueProcessor_PropertyChanged,
                QueueProcessor_Started,
                QueueProcessor_Stopped,
                QueueProcessor_ItemEnqueued,
                QueueProcessor_ItemDequeued);

            DownloadItemsView = CollectionViewSource.GetDefaultView(DownloadItemsCollection);
            DownloadItemsView.CurrentChanged += CollectionView_CurrentChanged;

            Progress = 0;
            BytesDownloadedThisSession = 0;
            Speed = null;
            Count = 0;
            ReadyCount = 0;
            DownloadingCount = 0;
            QueuedCount = 0;
            FinishedCount = 0;
            ErroredCount = 0;
            PausedCount = 0;
            Status = "Ready";

            _client = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(Settings.Default.ConnectionTimeout)
            };
            _progressReporter = new Progress<long>(value =>
            {
                Monitor.Enter(_bytesDownloadedLock);
                try
                {
                    BytesDownloadedThisSession += value;
                }
                finally
                {
                    Monitor.Exit(_bytesDownloadedLock);
                }
            });
            _clipboardService = new ClipboardObserver();
            _semaphoreUpdatingList = new SemaphoreSlim(1);
            _semaphoreRefreshingView = new SemaphoreSlim(1);
            _semaphoreMeasuringSpeed = new SemaphoreSlim(1);
            _showWindow = showWindow;
            _showPrompt = showPrompt;
            _ctsRefreshViewList = new();
            _ctsRefreshViewListLock = _ctsRefreshViewList;
            _downloadItemsCollectionLock = DownloadItemsCollection;
            _bytesDownloadedLock = BytesDownloadedThisSession;
            _bytesTransferredOverLifetimeLock = Settings.Default.BytesTransferredOverLifetime;
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
            SettingsCommand = new RelayCommand<object>(ShowSettings);
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
                CategoriesCollection.Add(cat);
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

            Settings.Default.LaunchCount++;

            // Populate history
            if (File.Exists(Paths.DownloadsHistoryFile))
            {
                Status = "Loading...";
                RaisePropertyChanged(nameof(Status));

                _ctsUpdatingList = new CancellationTokenSource();
                RaisePropertyChanged(nameof(IsBackgroundWorking));

                var ct = _ctsUpdatingList.Token;

                Task.Run(async () =>
                {
                    await _semaphoreUpdatingList.WaitAsync();

                    try
                    {
                        var source = Functions.Deserialize<SerializableDownloaderObjectModelList>(Paths.DownloadsHistoryFile);
                        var sourceObjects = source.Objects.ToArray();
                        var itemsToAdd = new List<DownloaderObjectModel>();
                        var itemsToEnqueue = new List<IQueueable>();
                        var total = sourceObjects.Length;

                        for (int i = 0; i < sourceObjects.Length; i++)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            if (sourceObjects[i] == null)
                            {
                                continue;
                            }

                            int progress = (int)((double)(i + 1) / total * 100);
                            Progress = progress;
                            Status = $"Loading {i + 1} of {total}: {Path.GetFileName(sourceObjects[i].Destination)}";
                            RaisePropertyChanged(nameof(Progress));
                            RaisePropertyChanged(nameof(Status));

                            var item = new DownloaderObjectModel(
                                _client,
                                sourceObjects[i].Url,
                                sourceObjects[i].Destination,
                                sourceObjects[i].DateCreated,
                                sourceObjects[i].TotalBytesToDownload,
                                sourceObjects[i].ConnLimit,
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

                            itemsToAdd.Add(item);
                        }

                        Status = "Refreshing...";
                        RaisePropertyChanged(nameof(Status));

                        AddObjects(itemsToAdd.ToArray());
                        QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                    }
                    catch
                    {

                    }
                    finally
                    {
                        _semaphoreUpdatingList.Release();
                    }

                    Progress = 0;
                    Status = "Ready";
                    RaisePropertyChanged(nameof(Progress));
                    RaisePropertyChanged(nameof(Status));
                }).ContinueWith(t =>
                {
                    _ctsUpdatingList.Dispose();
                    RaisePropertyChanged(nameof(IsBackgroundWorking));

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
                    where item.IsDownloading || item.IsPaused || item.IsErrored && item.SupportsResume
                    select item).Count() == items.Count();
        }

        private void Add(object obj)
        {
            AddDownloadViewModel addDownloadViewModel = new(_showWindow);
            DownloaderObjectModel[] itemsAdded = null;

            if (_showWindow.Invoke(addDownloadViewModel) == true)
            {
                _ctsUpdatingList = new CancellationTokenSource();
                RaisePropertyChanged(nameof(IsBackgroundWorking));

                var ct = _ctsUpdatingList.Token;

                Task.Run(async () =>
                {
                    itemsAdded = await AddItemsAsync(addDownloadViewModel.GeneratedUrls,
                        addDownloadViewModel.SaveToFolder,
                        addDownloadViewModel.Enqueue,
                        ct);
                }
                ).ContinueWith(async t =>
                {
                    _ctsUpdatingList.Dispose();
                    RaisePropertyChanged(nameof(IsBackgroundWorking));

                    RefreshCollectionView();

                    if (addDownloadViewModel.StartDownload)
                    {
                        await StartDownloadAsync(addDownloadViewModel.Enqueue, itemsAdded);
                    }
                });
            }
        }

        private bool Add_CanExecute(object obj)
        {
            return !IsBackgroundWorking;
        }

        private void Remove(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>();
            var itemsDeleteable = from item in items where item.IsCompleted select item;
            var delete = true;

            if (itemsDeleteable.Any())
            {
                var result = _showPrompt.Invoke(
                    $"Also delete the file{(items.Count() > 1 ? "s" : "")} from storage?",
                    "Remove",
                    PromptButton.YesNoCancel,
                    PromptIcon.Warning,
                    false);

                if (result == false)
                {
                    delete = false;
                }
                else if (result == null)
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
                RaisePropertyChanged(nameof(IsBackgroundWorking));

                RefreshCollectionView();
            });
        }

        private bool Remove_CanExecute(object obj)
        {
            if (obj == null || IsBackgroundWorking)
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
                var result = _showPrompt.Invoke(
                    "Opening too many files at the same file may cause the system to crash.\n\nProceed anyway?",
                    "Open",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false);

                if (result == false)
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
                var result = _showPrompt.Invoke(
                    "Opening too many folders at the same time may cause the system to crash.\n\nProceed anyway?",
                    "Open Folder",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false);

                if (result == false)
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

        private void ShowSettings(object obj)
        {
            var settingsViewModel = new SettingsViewModel();

            if (_showWindow.Invoke(settingsViewModel) == true)
            {
                if (settingsViewModel.ResetSettingsOnClose)
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
            RaisePropertyChanged(nameof(IsBackgroundWorking));

            var ct = _ctsUpdatingList.Token;

            var items = from item in DownloadItemsCollection where item.IsCompleted select item;

            Task.Run(async () => await RemoveObjectsAsync(items, false, ct)).ContinueWith(t =>
            {
                _ctsUpdatingList.Dispose();
                RaisePropertyChanged(nameof(IsBackgroundWorking));

                RefreshCollectionView();
            });
        }

        private bool ClearFinishedDownloads_CanExecute(object obj)
        {
            if (IsBackgroundWorking)
            {
                return false;
            }

            return FinishedCount > 0;
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
            return IsBackgroundWorking;
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
                if (_showPrompt.Invoke(
                    "Background operation in progress. Cancel and exit program?",
                    "Exit",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false) == false)
                {
                    return false;
                }

                try
                {
                    _ctsUpdatingList.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // background task may be done by the time cancellation is requested
                }
            }
            else if (DownloadingCount > 0)
            {
                var unPauseableDownloads = from item
                                           in DownloadItemsCollection
                                           where item.IsDownloading && !item.SupportsResume && item.BytesDownloaded > 0
                                           select item;

                if (unPauseableDownloads.Any())
                {
                    if (_showPrompt.Invoke(
                    "The following ongoing downloads cannot be paused and will be canceled. Proceed?\n\n"
                    + string.Join("\n", unPauseableDownloads.Select(o => o.Name).ToArray()),
                    "Exit",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false) == false)
                    {
                        return false;
                    }
                }
            }

            Status = "Saving...";
            RaisePropertyChanged(nameof(Status));

            Task<bool> closingTask = Task.Run(async () =>
            {
                if (QueueProcessor.IsBusy)
                {
                    await QueueProcessor.StopAsync();
                }

                await _semaphoreUpdatingList.WaitAsync();

                try
                {
                    var list = new SerializableDownloaderObjectModelList();
                    var index = 0;

                    Directory.CreateDirectory(Paths.LocalAppDataFolder);

                    foreach (var item in DownloadItemsCollection)
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
                            ConnLimit = item.ConnLimit,
                            IsQueued = QueueProcessor.IsQueued(item),
                            Status = item.Status,
                            DateCreated = item.DateCreated,
                            StatusCode = item.StatusCode
                        };

                        list.Objects.Add(sItem);
                    }

                    Functions.Serialize(list, Paths.DownloadsHistoryFile);
                }
                catch
                {
                    // close even when an exception occurs
                }
                finally
                {
                    _semaphoreUpdatingList.Release();
                }

                if (Settings.Default.FirstRun)
                {
                    Settings.Default.FirstRun = false;
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

            Monitor.Enter(_ctsRefreshViewListLock);

            foreach (var oldCts in _ctsRefreshViewList)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _ctsRefreshViewList.Clear();

            var newCts = new CancellationTokenSource();
            var ct = newCts.Token;

            _ctsRefreshViewList.Add(newCts);

            Monitor.Exit(_ctsRefreshViewListLock);

            Task.Run(async () =>
            {
                var semTask = _semaphoreRefreshingView.WaitAsync(ct);
                var throttle = Task.Delay(1000);
                try
                {
                    await semTask;
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        DownloadItemsView.Refresh();
                        CommandManager.InvalidateRequerySuggested();
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
            }, ct);
        }

        /// <summary>
        /// Creates new <see cref="DownloaderObjectModel"/>s from the params and adds them to the list.
        /// </summary>
        /// <param name="urls">The URLs to the files to add.</param>
        /// <param name="destination">The folder where to download the files.</param>
        /// <param name="enqueue">If <see langword="true"/>, the files will be added to the
        /// <see cref="QueueProcessor"/>.</param>
        /// <param name="ct">The <see cref="CancellationToken"/> to cancel the process.</param>
        /// <returns>An array of <see cref="DownloaderObjectModel"/>s which have been successfully
        /// added to the list.</returns>
        private async Task<DownloaderObjectModel[]> AddItemsAsync(IEnumerable<string> urls, string destination, bool enqueue, CancellationToken ct)
        {
            var existingUrls = from di in DownloadItemsCollection select di.Url;
            var existingDestinations = from di in DownloadItemsCollection select di.Destination;
            var itemsToAdd = new List<DownloaderObjectModel>();
            var itemsAdded = new List<DownloaderObjectModel>(); // the final list of items added; return this
            var itemsExist = new List<string>(); // skipped
            var itemsErrored = new List<string>(); // errored
            var wasCanceled = false;
            var totalItems = urls.Count();
            var counter = 0;

            await _semaphoreUpdatingList.WaitAsync();

            foreach (var url in urls.Distinct())
            {
                string fileName, filePath;

                counter++;

                Progress = (int)((double)counter / totalItems * 100);
                Status = $"Creating download {counter} of {totalItems}: {url}";
                RaisePropertyChanged(nameof(Status));
                RaisePropertyChanged(nameof(Progress));

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

                if (ct.IsCancellationRequested)
                {
                    wasCanceled = true;
                    break;
                }
            }

            if (!wasCanceled)
            {
                Status = "Refreshing...";
                RaisePropertyChanged(nameof(Status));

                AddObjects(itemsToAdd.ToArray());

                if (enqueue)
                {
                    QueueProcessor.Enqueue(itemsToAdd.ToArray());
                }

                itemsAdded.AddRange(itemsToAdd);
            }

            Progress = 0;
            Status = "Ready";
            RaisePropertyChanged(nameof(Progress));
            RaisePropertyChanged(nameof(Status));

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
            int total = objects.Length;

            Monitor.Enter(_downloadItemsCollectionLock);

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        if (objects[i] == null)
                        {
                            continue;
                        }

                        DownloadItemsCollection.Add(objects[i]);
                    }
                });
            }
            catch
            {

            }
            finally
            {
                Monitor.Exit(_downloadItemsCollectionLock);
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

                Status = $"{primaryStatus} {counter} of {total}: {item.Name}";
                Progress = (int)((double)counter / total * 100);
                RaisePropertyChanged(nameof(Status));
                RaisePropertyChanged(nameof(Progress));

                var queued = QueueProcessor.IsQueued(item);

                if (item.IsDownloading && queued)
                {
                    await QueueProcessor.StopAsync();
                }

                if (!item.IsCompleted && !item.IsReady)
                {
                    await item.CancelAsync();

                    if (item.TempFilesExist())
                    {
                        failed.Add(item.Destination);
                    }
                }
                else if (item.IsCompleted && delete)
                {
                    if (File.Exists(item.Destination))
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

            Status = "Refreshing...";
            RaisePropertyChanged(nameof(Status));

            Application.Current.Dispatcher.Invoke(() =>
            {
                Monitor.Enter(_downloadItemsCollectionLock);
                if (itemsToRemove.Count == DownloadItemsCollection.Count)
                {
                    DownloadItemsCollection.Clear();
                }
                else
                {
                    for (int i = 0; i < itemsToRemove.Count; i++)
                    {
                        DownloadItemsCollection.Remove(itemsToRemove[i]);
                    }
                }
                Monitor.Exit(_downloadItemsCollectionLock);
            });

            QueueProcessor.Dequeue(itemsToDequeue.ToArray());

            Progress = 0;
            Status = "Ready";
            RaisePropertyChanged(nameof(Progress));
            RaisePropertyChanged(nameof(Status));

            _semaphoreUpdatingList.Release();

            if (failed.Count > 0)
            {
                string title = delete ? "Delete" : "Remove";
                string description = $"The following files could not be {(delete ? "deleted" : "removed")} due to errors:";

                _showWindow(new ListViewerViewModel(failed, description, title));
            }
        }

        /// <summary>
        /// Starts downloading the specified items. If the number of items is larger than the
        /// maximum number of parallel downloads allowed, the items are enqueued instead.
        /// </summary>
        /// <param name="enqueue">Whether to enqueue the items instead of immediately starting the download.</param>
        /// <param name="items">The items to download.</param>
        /// <returns>A task that represents the successful completion of all the supplied items.</returns>
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

                Stopwatch stopwatch = new();
                long bytesFrom;
                long bytesCaptured;

                do
                {
                    stopwatch.Restart();
                    bytesFrom = BytesDownloadedThisSession;

                    await Task.Delay(1000);

                    bytesCaptured = BytesDownloadedThisSession - bytesFrom;
                    stopwatch.Stop();

                    Speed = (long)(bytesCaptured / ((double)stopwatch.ElapsedMilliseconds / 1000));
                    RaisePropertyChanged(nameof(Speed));
                    RaisePropertyChanged(nameof(BytesDownloadedThisSession));
                } while (DownloadingCount > 0);

                Speed = null;
                RaisePropertyChanged(nameof(Speed));

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
                    _showPrompt.Invoke(
                        "No new updates are available.",
                        "Update",
                        PromptButton.OK,
                        PromptIcon.Information);
                }
                return;
            }

            if (_showPrompt.Invoke(
                "An update is available.\n\nWould you like to download it now?",
                "Update",
                PromptButton.YesNo,
                PromptIcon.Information,
                true) == true)
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
            if (DownloadingCount == 0)
            {
                Status = "Ready";
                RaisePropertyChanged(nameof(Status));
            }

            RefreshCollectionView();

            Monitor.Enter(_bytesTransferredOverLifetimeLock);
            try
            {
                Settings.Default.BytesTransferredOverLifetime +=
                    (ulong)(sender as DownloaderObjectModel).BytesDownloadedThisSession;
            }
            finally
            {
                Monitor.Exit(_bytesTransferredOverLifetimeLock);
            }
        }

        private void CollectionView_CurrentChanged(object sender, EventArgs e)
        {
            Count = DownloadItemsCollection.Count;
            QueuedCount = QueueProcessor.Count;
            ReadyCount = DownloadItemsCollection.Count(o => o.IsReady && !QueueProcessor.IsQueued(o));
            DownloadingCount = DownloadItemsCollection.Count(o => o.IsDownloading);
            PausedCount = DownloadItemsCollection.Count(o => o.IsPaused);
            FinishedCount = DownloadItemsCollection.Count(o => o.IsCompleted);
            ErroredCount = DownloadItemsCollection.Count(o => o.IsErrored);

            RaisePropertyChanged(nameof(Count));
            RaisePropertyChanged(nameof(DownloadingCount));
            RaisePropertyChanged(nameof(ErroredCount));
            RaisePropertyChanged(nameof(FinishedCount));
            RaisePropertyChanged(nameof(PausedCount));
            RaisePropertyChanged(nameof(QueuedCount));
            RaisePropertyChanged(nameof(ReadyCount));
            RaisePropertyChanged(nameof(IsDownloading));

            if (!IsBackgroundWorking)
            {
                if (DownloadingCount > 0)
                {
                    Status = $"{DownloadingCount} item{(DownloadingCount > 1 ? "s" : "")} downloading";
                }
                else
                {
                    if (_semaphoreUpdatingList.CurrentCount > 0)
                    {
                        Status = "Ready";
                    }
                }

                RaisePropertyChanged(nameof(Status));
            }
        }

        #endregion    
    }
}