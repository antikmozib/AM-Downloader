using AMDownloader.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace AMDownloader
{
    class OptionsViewModel
    {
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand ResetSettingsCommand { get; private set; }

        public OptionsViewModel()
        {
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ResetSettingsCommand = new RelayCommand(ResetSettings);
        }

        void SaveSettings(object obj)
        {
            if (Settings.Default.MaxConnectionsPerDownload < 1 || Settings.Default.MaxConnectionsPerDownload > 5)
            {
                Settings.Default.MaxConnectionsPerDownload = 5;
            }
            if (Settings.Default.MaxParallelDownloads < 1 || Settings.Default.MaxParallelDownloads > 10)
            {
                Settings.Default.MaxParallelDownloads = 10;
            }
            Settings.Default.Save();
        }

        void ResetSettings(object obj)
        {
            Settings.Default.Reset();
            if (Directory.Exists(Common.ApplicationPaths.DownloadsHistory))
            {
                try
                {
                    Directory.Delete(Common.ApplicationPaths.DownloadsHistory, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
            if (Directory.Exists(Common.ApplicationPaths.SavedLocationsHistory))
            {
                try
                {
                    Directory.Delete(Common.ApplicationPaths.SavedLocationsHistory, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        }
    }
}
