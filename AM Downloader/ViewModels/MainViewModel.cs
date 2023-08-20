// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.Models.Serialization;
using AMDownloader.Properties;
using AMDownloader.QueueProcessing;
using AMDownloader.Updating;
using Microsoft.VisualBasic.FileIO;
using Serilog;
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
    public delegate bool? ShowPromptDelegate(
        string promptText,
        string caption = "",
        PromptButton button = PromptButton.OK,
        PromptIcon icon = PromptIcon.None,
        bool defaultResult = true);

    public delegate void NotifyUpdateAvailableDelegate(UpdateInfo latestUpdateInfo, bool showReminderButton = false);

    public enum Category
    {
        All, Ready, Queued, Downloading, Paused, Finished, Errored
    }

    public class MainViewModel : INotifyPropertyChanged, ICloseable
    {
        #region Fields

        private readonly HttpClient _client;
        private readonly IProgress<long> _progressReporter;
        private readonly SemaphoreSlim _refreshingViewSemaphore;
        private readonly Func<object, bool?> _showWindow;
        private readonly ShowPromptDelegate _showPrompt;
        private readonly NotifyUpdateAvailableDelegate _notifyUpdateAvailable;
        private CancellationTokenSource _updatingListCts;
        private readonly List<CancellationTokenSource> _refreshViewCtsList;
        private TaskCompletionSource _updatingListTcs;
        private TaskCompletionSource _refreshingViewTcs;
        private TaskCompletionSource _reportingSpeedTcs;
        private TaskCompletionSource _closingTcs;
        private TaskCompletionSource _triggerUpdateCheckTcs;
        private readonly object _refreshViewCtsListLock;
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
        public string Status { get; private set; }
        public bool IsDownloading => DownloadingCount > 0;
        public bool IsBackgroundWorking => _updatingListTcs != null
            && _updatingListTcs.Task.Status != TaskStatus.RanToCompletion;
        public bool IsCheckingForUpdates => _triggerUpdateCheckTcs != null
            && _triggerUpdateCheckTcs.Task.Status != TaskStatus.RanToCompletion;

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Closing;
        public event EventHandler Closed;

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

        public MainViewModel(Func<object, bool?> showWindow,
            ShowPromptDelegate showPrompt,
            NotifyUpdateAvailableDelegate notifyUpdateAvailable,
            EventHandler closing,
            EventHandler closed)
        {
            DownloadItemsCollection = new ObservableCollection<DownloaderObjectModel>();

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

            Closing += closing;
            Closed += closed;

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
            _refreshingViewSemaphore = new SemaphoreSlim(1);
            _showWindow = showWindow;
            _showPrompt = showPrompt;
            _notifyUpdateAvailable = notifyUpdateAvailable;
            _refreshViewCtsList = new List<CancellationTokenSource>();
            _refreshViewCtsListLock = _refreshViewCtsList;
            _downloadItemsCollectionLock = DownloadItemsCollection;
            _bytesDownloadedLock = BytesDownloadedThisSession;
            _bytesTransferredOverLifetimeLock = Settings.Default.BytesTransferredOverLifetime;
            _resetAllSettingsOnClose = false;

            AddCommand = new RelayCommand(Add, Add_CanExecute);
            StartCommand = new RelayCommand<object>(Start, Start_CanExecute);
            RemoveCommand = new RelayCommand<object>(Remove, Remove_CanExecute);
            CancelCommand = new RelayCommand<object>(Cancel, Cancel_CanExecute);
            PauseCommand = new RelayCommand<object>(Pause, Pause_CanExecute);
            OpenCommand = new RelayCommand<object>(Open, Open_CanExecute);
            OpenContainingFolderCommand = new RelayCommand<object>(OpenContainingFolder, OpenContainingFolder_CanExecute);
            StartQueueCommand = new RelayCommand(StartQueue, StartQueue_CanExecute);
            StopQueueCommand = new RelayCommand(StopQueue, StopQueue_CanExecute);
            CategoryChangedCommand = new RelayCommand<object>(CategoryChanged);
            SettingsCommand = new RelayCommand(ShowSettings);
            EnqueueCommand = new RelayCommand<object>(Enqueue, Enqueue_CanExecute);
            DequeueCommand = new RelayCommand<object>(Dequeue, Dequeue_CanExecute);
            CopyLinkToClipboardCommand = new RelayCommand<object>(CopyLinkToClipboard, CopyLinkToClipboardCommand_CanExecute);
            ClearFinishedDownloadsCommand = new RelayCommand(ClearFinishedDownloads, ClearFinishedDownloads_CanExecute);
            CancelBackgroundTaskCommand = new RelayCommand(CancelBackgroundTask, CancelBackgroundTask_CanExecute);
            CheckForUpdatesCommand = new RelayCommand<object>(CheckForUpdates, CheckForUpdates_CanExecute);
            UIClosedCommand = new RelayCommand(UIClosed);

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
                CheckForUpdates(true);
            }

            // Populate history
            if (File.Exists(Common.Paths.DownloadsHistoryFile))
            {
                Status = "Loading...";
                _updatingListTcs = new TaskCompletionSource();
                _updatingListCts = new CancellationTokenSource();
                RaisePropertyChanged(nameof(Status));
                RaisePropertyChanged(nameof(IsBackgroundWorking));

                var ct = _updatingListCts.Token;

                Task.Run(() =>
                {
                    try
                    {
                        var source = Common.Functions.Deserialize<SerializingDownloaderObjectModelList>(Common.Paths.DownloadsHistoryFile);
                        var sourceObjects = source.Objects.ToArray();
                        var itemsToAdd = new List<DownloaderObjectModel>();
                        var itemsToEnqueue = new List<IQueueable>();
                        var total = sourceObjects.Length;
                        var progress = 0;

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

                            progress = (int)((double)(i + 1) / total * 100);
                            Progress = progress;
                            Status = $"Loading {i + 1} of {total}: {Path.GetFileName(sourceObjects[i].Destination)}";
                            RaisePropertyChanged(nameof(Progress));
                            RaisePropertyChanged(nameof(Status));

                            var item = new DownloaderObjectModel(
                                httpClient: _client,
                                url: sourceObjects[i].Url,
                                destination: sourceObjects[i].Destination,
                                dateCreated: sourceObjects[i].DateCreated,
                                dateFinished: sourceObjects[i].DateFinished,
                                bytesToDownload: sourceObjects[i].TotalBytesToDownload,
                                connLimit: sourceObjects[i].ConnLimit,
                                httpStatusCode: sourceObjects[i].StatusCode,
                                status: sourceObjects[i].Status,
                                downloadCreated: Download_Created,
                                downloadStarted: Download_Started,
                                downloadStopped: Download_Stopped,
                                propertyChanged: Download_PropertyChanged,
                                bytesReporter: _progressReporter);

                            if (sourceObjects[i].IsQueued)
                            {
                                itemsToEnqueue.Add(item);
                            }

                            itemsToAdd.Add(item);
                        }

                        Progress = 0;
                        Status = "Updating...";
                        RaisePropertyChanged(nameof(Progress));
                        RaisePropertyChanged(nameof(Status));

                        AddObjects(itemsToAdd.ToArray());
                        QueueProcessor.Enqueue(itemsToEnqueue.ToArray());
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, ex.Message);
                    }
                }).ContinueWith(async (t) =>
                {
                    _updatingListCts.Dispose();
                    _updatingListTcs.SetResult();

                    Status = "Refreshing...";
                    RaisePropertyChanged(nameof(IsBackgroundWorking));
                    RaisePropertyChanged(nameof(Status));

                    await RefreshCollectionViewAsync();
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            Task.Run(async () => await StartDownloadAsync(false, items));
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            QueueProcessor.Dequeue(items);
            Parallel.ForEach(items, (item) => item.Pause());

            RefreshCollectionView();
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            QueueProcessor.Dequeue(items);
            Parallel.ForEach(items, (item) => item.Cancel());

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

        private void Add()
        {
            AddDownloadViewModel addDownloadViewModel = new();
            DownloaderObjectModel[] itemsCreated = null;

            _showWindow(addDownloadViewModel);

            if (addDownloadViewModel.ItemsAdded)
            {
                _updatingListTcs = new TaskCompletionSource();
                _updatingListCts = new CancellationTokenSource();
                RaisePropertyChanged(nameof(IsBackgroundWorking));

                var ct = _updatingListCts.Token;

                Task.Run(() =>
                {
                    itemsCreated = CreateObjects(
                        addDownloadViewModel.ExplodedUrls,
                        addDownloadViewModel.SaveLocation,
                        ct);

                    Status = "Updating...";
                    RaisePropertyChanged(nameof(Status));

                    AddObjects(itemsCreated);

                    if (addDownloadViewModel.Enqueue)
                    {
                        QueueProcessor.Enqueue(itemsCreated);
                    }

                }).ContinueWith(async (t) =>
                {
                    _updatingListCts.Dispose();
                    _updatingListTcs.SetResult();

                    Status = "Refreshing...";
                    RaisePropertyChanged(nameof(IsBackgroundWorking));
                    RaisePropertyChanged(nameof(Status));

                    await RefreshCollectionViewAsync();

                    if (addDownloadViewModel.StartDownload && itemsCreated.Length > 0)
                    {
                        await StartDownloadAsync(addDownloadViewModel.Enqueue, itemsCreated);
                    }
                });
            }
        }

        private bool Add_CanExecute()
        {
            return !IsBackgroundWorking;
        }

        private void Remove(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            var itemsDeleteable = from item in items where item.IsCompleted select item;
            var delete = true;

            if (itemsDeleteable.Any())
            {
                var result = _showPrompt(
                    $"Also delete the file{(items.Length > 1 ? "s" : "")} from storage?",
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

            _updatingListTcs = new TaskCompletionSource();
            _updatingListCts = new CancellationTokenSource();
            RaisePropertyChanged(nameof(IsBackgroundWorking));

            var ct = _updatingListCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await RemoveObjectsAsync(items, delete, ct);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                }
            }).ContinueWith(async (t) =>
            {
                _updatingListCts.Dispose();
                _updatingListTcs.SetResult();

                Status = "Refreshing...";
                RaisePropertyChanged(nameof(IsBackgroundWorking));
                RaisePropertyChanged(nameof(Status));

                await RefreshCollectionViewAsync();
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            if (items.Length > 1)
            {
                if (_showPrompt(
                     "Opening too many files at the same file may cause the system to crash.\n\nProceed anyway?",
                     "Open",
                     PromptButton.YesNo,
                     PromptIcon.Exclamation,
                     false) == false)
                {
                    return;
                }
            }

            Parallel.ForEach(items, (item) => Process.Start("explorer.exe", "\"" + item.Destination + "\""));
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            var itemsOpenable = (from item in items
                                 where File.Exists(item.Destination) ||
                                 Directory.Exists(Path.GetDirectoryName(item.Destination))
                                 select item).ToArray();

            if (itemsOpenable.Length > 1)
            {
                if (_showPrompt(
                    "Opening too many folders at the same time may cause the system to crash.\n\nProceed anyway?",
                    "Open Folder",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false) == false)
                {
                    return;
                }
            }

            Parallel.ForEach(itemsOpenable, (item) =>
            {
                string explorerParam = string.Empty;

                if (File.Exists(item.Destination))
                {
                    explorerParam = "/select, \"\"" + item.Destination + "\"\"";
                }
                else if (Directory.Exists(Path.GetDirectoryName(item.Destination)))
                {
                    explorerParam = Path.GetDirectoryName(item.Destination);
                }

                if (!string.IsNullOrWhiteSpace(explorerParam))
                {
                    Process.Start("explorer.exe", explorerParam);
                }
            });
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

        private void StartQueue()
        {
            Task.Run(async () =>
            {
                await QueueProcessor.StartAsync();
            });
        }

        private bool StartQueue_CanExecute()
        {
            return !QueueProcessor.IsBusy && QueueProcessor.Count > 0;
        }

        private void StopQueue()
        {
            QueueProcessor.Stop();
        }

        private bool StopQueue_CanExecute()
        {
            return QueueProcessor.IsBusy;
        }

        private void ShowSettings()
        {
            var settingsViewModel = new SettingsViewModel();

            if (_showWindow(settingsViewModel) == true)
            {
                if (settingsViewModel.ResetSettingsOnClose)
                {
                    _resetAllSettingsOnClose = true;
                }
            }
        }

        private void Enqueue(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            QueueProcessor.Enqueue(items);
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();

            QueueProcessor.Dequeue(items);
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
            var items = (obj as ObservableCollection<object>).Cast<DownloaderObjectModel>().ToArray();
            var clipText = string.Empty;
            var counter = 0;
            var total = items.Length;

            foreach (var item in items)
            {
                clipText += item.Url;
                if (counter < total - 1)
                {
                    clipText += Environment.NewLine;
                }
                counter++;
            }

            ClipboardObserver.SetText(clipText);
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

        private void ClearFinishedDownloads()
        {
            _updatingListTcs = new TaskCompletionSource();
            _updatingListCts = new CancellationTokenSource();
            RaisePropertyChanged(nameof(IsBackgroundWorking));

            var ct = _updatingListCts.Token;
            var items = (from item in DownloadItemsCollection where item.IsCompleted select item).ToArray();

            Task.Run(async () =>
            {
                try
                {
                    await RemoveObjectsAsync(items, false, ct);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                }
            }).ContinueWith(t =>
            {
                _updatingListCts.Dispose();
                _updatingListTcs.SetResult();

                RaisePropertyChanged(nameof(IsBackgroundWorking));

                RefreshCollectionView();
            });
        }

        private bool ClearFinishedDownloads_CanExecute()
        {
            return !IsBackgroundWorking && FinishedCount > 0;
        }

        private void CancelBackgroundTask()
        {
            try
            {
                _updatingListCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already canceled
            }
        }

        private bool CancelBackgroundTask_CanExecute()
        {
            return IsBackgroundWorking;
        }

        private void CheckForUpdates(object obj)
        {
            bool silent = (bool)obj;

            _triggerUpdateCheckTcs = new TaskCompletionSource();

            Task.Run(async () => await TriggerUpdateCheckAsync(silent))
                .ContinueWith(t =>
                {
                    _triggerUpdateCheckTcs.SetResult();

                    RaisePropertyChanged(nameof(IsCheckingForUpdates));
                });
        }

        private bool CheckForUpdates_CanExecute(object obj)
        {
            return !IsCheckingForUpdates;
        }

        private void UIClosed()
        {
            if (_resetAllSettingsOnClose)
            {
                Common.Functions.ResetAllSettings();
            }
        }

        #endregion

        #region Public methods

        public void Close()
        {
            if (_closingTcs != null && _closingTcs.Task.Status != TaskStatus.RanToCompletion)
            {
                // already closing
                return;
            }

            if (IsBackgroundWorking)
            {
                if (_showPrompt(
                    "Background operation in progress. Cancel and exit program?",
                    "Exit",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false) == false)
                {
                    return;
                }

                try
                {
                    _updatingListCts.Cancel();
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
                    if (_showPrompt(
                    "The following ongoing downloads cannot be paused and will be canceled. Proceed?\n\n"
                    + string.Join("\n", unPauseableDownloads.Select(o => o.Name).ToArray()),
                    "Exit",
                    PromptButton.YesNo,
                    PromptIcon.Exclamation,
                    false) == false)
                    {
                        return;
                    }
                }
            }

            _closingTcs = new TaskCompletionSource();
            RaiseEvent(Closing);

            Task.Run(async () =>
            {
                if (IsBackgroundWorking)
                {
                    await _updatingListTcs.Task;
                }

                if (QueueProcessor.IsBusy)
                {
                    await QueueProcessor.StopAsync();
                }

                Status = "Saving...";
                RaisePropertyChanged(nameof(Status));

                try
                {
                    var list = new SerializingDownloaderObjectModelList();
                    var index = 0;

                    Directory.CreateDirectory(Common.Paths.LocalAppDataFolder);

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

                        var sItem = new SerializingDownloaderObjectModel
                        {
                            Index = index++,
                            Url = item.Url,
                            Destination = item.Destination,
                            DateCreated = item.DateCreated,
                            DateFinished = item.DateFinished,
                            TotalBytesToDownload = item.TotalBytesToDownload,
                            ConnLimit = item.ConnLimit,
                            StatusCode = item.StatusCode,
                            Status = item.Status,
                            IsQueued = QueueProcessor.IsQueued(item)
                        };

                        list.Objects.Add(sItem);
                    }

                    Common.Functions.Serialize(list, Common.Paths.DownloadsHistoryFile);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);

                    // close even when an exception occurs
                }

                if (Settings.Default.FirstRun)
                {
                    Settings.Default.FirstRun = false;
                }

                Settings.Default.Save();

                _closingTcs.SetResult();
                RaiseEvent(Closed);
            });
        }

        #endregion

        #region Private methods

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        protected virtual void RaiseEvent(EventHandler handler)
        {
            handler?.Invoke(this, null);
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
            if (IsBackgroundWorking)
            {
                return;
            }

            // cancel all pending refreshes

            Monitor.Enter(_refreshViewCtsListLock);

            foreach (var oldCts in _refreshViewCtsList)
            {
                try
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                }
            }

            _refreshViewCtsList.Clear();

            _refreshingViewTcs = new TaskCompletionSource();
            var newCts = new CancellationTokenSource();
            var ct = newCts.Token;

            _refreshViewCtsList.Add(newCts);

            Monitor.Exit(_refreshViewCtsListLock);

            Task.Run(async () =>
            {
                var semTask = _refreshingViewSemaphore.WaitAsync(ct);
                var throttle = Task.Delay(2000);

                try
                {
                    await semTask;

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        DownloadItemsView.Refresh();
                        CommandManager.InvalidateRequerySuggested();
                    });

                    await throttle;

                    if (_refreshingViewTcs.Task.Status != TaskStatus.RanToCompletion)
                    {
                        _refreshingViewTcs.SetResult();
                    }

                    Log.Debug($"{nameof(DownloadItemsView)} refreshed.");
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                }
                finally
                {
                    if (semTask.Status == TaskStatus.RanToCompletion)
                    {
                        _refreshingViewSemaphore.Release();
                    }
                }
            }, ct);
        }

        private async Task RefreshCollectionViewAsync()
        {
            RefreshCollectionView();
            await _refreshingViewTcs.Task;
        }

        /// <summary>
        /// Creates new <see cref="DownloaderObjectModel"/>s from the params.
        /// </summary>
        /// <param name="urls">The URLs to the files to add.</param>
        /// <param name="saveToFolder">The folder where to download the files.</param>
        /// <param name="ct">The <see cref="CancellationToken"/> to cancel the process.</param>
        /// <returns>An array of <see cref="DownloaderObjectModel"/>s which have been successfully
        /// created.</returns>
        private DownloaderObjectModel[] CreateObjects(IEnumerable<string> urls, string saveToFolder, CancellationToken ct)
        {
            var existingItems = DownloadItemsCollection.ToList();
            var itemsCreated = new List<DownloaderObjectModel>();
            var itemsExist = new List<string>(); // skipped
            var itemsErrored = new List<string>(); // errored
            var totalItems = urls.Count();
            var counter = 0;

            foreach (var url in urls.Distinct())
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                string fileName, filePath;

                counter++;

                Progress = (int)((double)counter / totalItems * 100);
                Status = $"Adding {counter} of {totalItems}: {url}";
                RaisePropertyChanged(nameof(Status));
                RaisePropertyChanged(nameof(Progress));

                fileName = Common.Functions.GetFileNameFromUrl(url);

                if (string.IsNullOrEmpty(fileName))
                {
                    itemsErrored.Add(url);
                    continue;
                }

                // check if an item already exists with the same url and destination
                if (existingItems.Where(o =>
                    o.Url.ToLower() == url.ToLower()
                    && Path.GetDirectoryName(o.Destination.ToLower()) == saveToFolder.ToLower()).Any())
                {
                    itemsExist.Add(url);
                    continue;
                }

                filePath = GenerateNewDestination(
                    Path.Combine(saveToFolder, Common.Functions.GetFileNameFromUrl(url)),
                    existingItems.Select(o => o.Destination).ToArray());

                DownloaderObjectModel item;
                item = new DownloaderObjectModel(
                        httpClient: _client,
                        url: url,
                        destination: filePath,
                        downloadCreated: Download_Created,
                        downloadStarted: Download_Started,
                        downloadStopped: Download_Stopped,
                        propertyChanged: Download_PropertyChanged,
                        bytesReporter: _progressReporter);

                existingItems.Add(item);
                itemsCreated.Add(item);
            }

            if (itemsExist.Count > 0)
            {
                _showWindow(new ListViewerViewModel(itemsExist,
                    "The following URLs were not added because they are already in the list:",
                    "Duplicate Entries"));
            }

            if (itemsErrored.Count > 0)
            {
                _showWindow(new ListViewerViewModel(itemsErrored,
                    "The following URLs were not added because they are invalid:",
                    "Invalid Entries"));
            }

            Progress = 0;
            RaisePropertyChanged(nameof(Progress));

            return itemsCreated.ToArray();
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
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
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

            foreach (var item in items)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

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
            }

            Status = "Updating...";
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
            RaisePropertyChanged(nameof(Progress));

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
            if (_reportingSpeedTcs != null && _reportingSpeedTcs.Task.Status != TaskStatus.RanToCompletion)
            {
                // already reporting speed
                return;
            }

            _reportingSpeedTcs = new TaskCompletionSource();

            Task.Run(async () =>
            {
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
                _reportingSpeedTcs.SetResult();
                RaisePropertyChanged(nameof(Speed));
            });
        }

        private async Task TriggerUpdateCheckAsync(bool silent)
        {
            try
            {
                var appName = Assembly.GetExecutingAssembly().GetName().Name;
                var currentVer = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                var latestUpdateInfo = await UpdateService.GetLatestUpdateInfoAsync(appName, _client);

                if (UpdateService.IsUpdateAvailable(latestUpdateInfo.Versions, currentVer))
                {
                    _notifyUpdateAvailable(latestUpdateInfo, silent);
                }
                else
                {
                    if (!silent)
                    {
                        _showPrompt(
                            (string)Application.Current.FindResource("noUpdateMsg"),
                            "Update",
                            icon: PromptIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);

                if (!silent)
                {
                    _showPrompt(
                        string.Format((string)Application.Current.FindResource("erroredUpdateMsg"), ex.Message),
                        "Update",
                        icon: PromptIcon.Error);
                }
            }
        }

        /// <summary>
        /// Generates a new path if <paramref name="originalDestination"/> already exists on disk or in
        /// <paramref name="existingDestinations"/>.
        /// </summary>
        /// <param name="originalDestination">The path to the file for which to generate a new path.</param>
        /// <param name="existingDestinations">The list of existing paths against which to perform checks.</param>
        /// <returns>A new path which is guaranteed to not exist on disk or in <paramref name="existingDestinations"/>.
        /// </returns>
        private static string GenerateNewDestination(string originalDestination, string[] existingDestinations)
        {
            string dirPath = Path.GetDirectoryName(originalDestination);
            string newDestination = originalDestination;
            int i = 0;

            existingDestinations = existingDestinations.Select(o => o.ToLower()).ToArray();

            while (File.Exists(newDestination) || existingDestinations.Contains(newDestination.ToLower()))
            {
                newDestination = Path.Combine(
                    dirPath,
                    $"{Path.GetFileNameWithoutExtension(originalDestination)} ({++i}){Path.GetExtension(originalDestination)}");
            }

            return newDestination;
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
                    Status = "Ready";
                }

                RaisePropertyChanged(nameof(Status));
            }
        }

        #endregion    
    }
}