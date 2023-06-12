// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using AMDownloader.Properties;
using System.Windows.Input;

namespace AMDownloader
{
    internal class OptionsViewModel
    {
        public int MaxParallelDownloads
        {
            get => Settings.Default.MaxParallelDownloads;

            set
            {
                if (value < 1)
                {
                    Settings.Default.MaxParallelDownloads = 1;
                }
                else if (value > Constants.ParallelDownloadsLimit)
                {
                    Settings.Default.MaxParallelDownloads = Constants.ParallelDownloadsLimit;
                }
                else
                {
                    Settings.Default.MaxParallelDownloads = value;
                }
            }
        }

        public long MaxDownloadSpeed
        {
            get => Settings.Default.MaxDownloadSpeed;

            set
            {
                if (value < 0)
                {
                    Settings.Default.MaxDownloadSpeed = 0;
                }
                else if (value > long.MaxValue)
                {
                    Settings.Default.MaxDownloadSpeed = long.MaxValue;
                }
                else
                {
                    Settings.Default.MaxDownloadSpeed = value;
                }
            }
        }

        public bool ResetSettingsOnClose;

        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand ResetSettingsCommand { get; private set; }

        public OptionsViewModel()
        {
            ResetSettingsOnClose = false;

            SaveSettingsCommand = new RelayCommand<object>(SaveSettings);
            ResetSettingsCommand = new RelayCommand<object>(ResetSettings);
        }

        private void SaveSettings(object obj)
        {
        }

        private void ResetSettings(object obj)
        {
            Settings.Default.Reset();
            ResetSettingsOnClose = true;
        }
    }
}