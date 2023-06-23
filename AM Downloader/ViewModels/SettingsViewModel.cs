// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Properties;
using System.Windows.Input;

namespace AMDownloader.ViewModels
{
    internal class SettingsViewModel
    {
        #region Fields

        private int _maxParallelDownloads;
        private int _maxParallelConnPerDownload;
        private long _maxDownloadSpeed;
        private long _connectionTimeout;

        #endregion

        #region Properties

        public int MaxParallelDownloads
        {
            get => _maxParallelDownloads;

            set
            {
                if (value < 1)
                {
                    _maxParallelDownloads = 1;
                }
                else if (value > Constants.ParallelDownloadsLimit)
                {
                    _maxParallelDownloads = Constants.ParallelDownloadsLimit;
                }
                else
                {
                    _maxParallelDownloads = value;
                }
            }
        }

        public int MaxParallelConnPerDownload
        {
            get => _maxParallelConnPerDownload;

            set
            {
                if (value < 1)
                {
                    _maxParallelConnPerDownload = 1;
                }
                else if (value > Constants.ParallelConnPerDownloadLimit)
                {
                    _maxParallelConnPerDownload = Constants.ParallelConnPerDownloadLimit;
                }
                else
                {
                    _maxParallelConnPerDownload = value;
                }
            }
        }

        public long MaxDownloadSpeed
        {
            get => _maxDownloadSpeed;

            set
            {
                if (value < 0)
                {
                    _maxDownloadSpeed = 0;
                }
                else if (value > long.MaxValue)
                {
                    _maxDownloadSpeed = long.MaxValue;
                }
                else
                {
                    _maxDownloadSpeed = value;
                }
            }
        }

        public long ConnectionTimeout
        {
            get => _connectionTimeout;
            set
            {
                if (value < 0)
                {
                    _connectionTimeout = 0;
                }
                else if (value > long.MaxValue)
                {
                    _connectionTimeout = long.MaxValue;
                }
                else
                {
                    _connectionTimeout = value;
                }
            }
        }

        public bool ClearFinishedDownloadsOnExit { get; set; }

        public bool RememberLastDownloadLocation { get; set; }

        public bool AutoCheckForUpdates { get; set; }

        public bool ResetSettingsOnClose { get; set; }

        #endregion

        #region Commands

        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand ResetCommand { get; private set; }

        #endregion

        #region Ctors

        public SettingsViewModel()
        {
            _maxParallelDownloads = Settings.Default.MaxParallelDownloads;
            _maxParallelConnPerDownload = Settings.Default.MaxParallelConnPerDownload;
            _maxDownloadSpeed = Settings.Default.MaxDownloadSpeed;
            _connectionTimeout = Settings.Default.ConnectionTimeout;

            ClearFinishedDownloadsOnExit = Settings.Default.ClearFinishedDownloadsOnExit;
            RememberLastDownloadLocation = Settings.Default.RememberLastDownloadLocation;
            AutoCheckForUpdates = Settings.Default.AutoCheckForUpdates;

            ResetSettingsOnClose = false;

            SaveCommand = new RelayCommand<object>(Save);
            CancelCommand = new RelayCommand<object>(Cancel);
            ResetCommand = new RelayCommand<object>(Reset);
        }

        #endregion

        #region Private methods

        private void Save(object obj)
        {
            Settings.Default.MaxParallelDownloads = _maxParallelDownloads;
            Settings.Default.MaxDownloadSpeed = _maxDownloadSpeed;
            Settings.Default.ConnectionTimeout = _connectionTimeout;
            Settings.Default.ClearFinishedDownloadsOnExit = ClearFinishedDownloadsOnExit;
            Settings.Default.RememberLastDownloadLocation = RememberLastDownloadLocation;
            Settings.Default.AutoCheckForUpdates = AutoCheckForUpdates;
            Settings.Default.Save();
        }

        private void Cancel(object obj)
        {
        }

        private void Reset(object obj)
        {
            Settings.Default.Reset();

            ResetSettingsOnClose = true;
        }

        #endregion
    }
}