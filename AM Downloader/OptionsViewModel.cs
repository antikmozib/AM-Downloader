// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

using AMDownloader.Common;
using AMDownloader.Properties;
using System.IO;
using System.Windows.Input;

namespace AMDownloader
{
    internal class OptionsViewModel
    {
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand ResetSettingsCommand { get; private set; }

        public OptionsViewModel()
        {
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
            if (Directory.Exists(Common.AppPaths.LocalAppData))
            {
                try
                {
                    Directory.Delete(Common.AppPaths.LocalAppData, true);
                }
                catch { }
            }
        }
    }
}