// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using AMDownloader.Properties;
using System.IO;
using System.Windows.Input;

namespace AMDownloader
{
    internal class OptionsViewModel
    {
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
            if (Settings.Default.MaxParallelDownloads < 1 || Settings.Default.MaxParallelDownloads > AppConstants.ParallelDownloadsLimit)
            {
                Settings.Default.MaxParallelDownloads = AppConstants.ParallelDownloadsLimit;
            }
            Settings.Default.Save();
        }

        private void ResetSettings(object obj)
        {
            Settings.Default.Reset();
            ResetSettingsOnClose = true;
        }
    }
}