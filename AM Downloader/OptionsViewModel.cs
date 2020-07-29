using AMDownloader.Properties;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace AMDownloader
{
    class OptionsViewModel
    {
        public ICommand SaveSettingsCommand { get; private set; }

        public OptionsViewModel()
        {
            SaveSettingsCommand = new RelayCommand(SaveSettings);
        }

        void SaveSettings(object obj)
        {
            Settings.Default.Save();
        }
    }
}
