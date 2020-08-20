using System.IO;
using System.Windows.Input;
using AMDownloader.Common;
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
            if (Settings.Default.MaxConnectionsPerDownload < 1 || Settings.Default.MaxConnectionsPerDownload > AppConstants.ParallelStreamsLimit)
            {
                Settings.Default.MaxConnectionsPerDownload = AppConstants.ParallelStreamsLimit;
            }
            if (Settings.Default.MaxParallelDownloads < 1 || Settings.Default.MaxParallelDownloads > AppConstants.ParallelDownloadsLimit)
            {
                Settings.Default.MaxParallelDownloads = AppConstants.ParallelDownloadsLimit;
            }
            Settings.Default.Save();
        }

        void ResetSettings(object obj)
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
