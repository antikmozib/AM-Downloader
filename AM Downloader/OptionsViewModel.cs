using System.IO;
using System.Windows.Input;
using AMDownloader.Properties;

namespace AMDownloader
{    
    class OptionsViewModel
    {
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand ResetSettingsCommand { get; private set; }

        public OptionsViewModel()
        {
            SaveSettingsCommand = new RelayCommand<object>(SaveSettings);
            ResetSettingsCommand = new RelayCommand<object>(ResetSettings);
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
            if (Directory.Exists(Common.ApplicationPaths.LocalAppData))
            {
                try
                {
                    Directory.Delete(Common.ApplicationPaths.LocalAppData, true);
                }
                catch { }
            }
        }
    }
}
