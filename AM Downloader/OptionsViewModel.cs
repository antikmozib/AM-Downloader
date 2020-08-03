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
            Settings.Default.Save();
        }

        void ResetSettings(object obj)
        {
            Settings.Default.Reset();
            try
            {
                Directory.Delete(Common.ApplicationPaths.DownloadsHistory,true);
                Directory.Delete(Common.ApplicationPaths.SavedLocationsHistory,true);
            }
            catch
            {

            }
        }
    }
}
