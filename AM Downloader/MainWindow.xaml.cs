using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AMDownloader.Properties;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DownloaderViewModel primaryViewModel;
        private static readonly string appGuid = "20d3be33-cd45-4c69-b038-e95bc434e09c";
        private static readonly Mutex mutex = new Mutex(false, "Global\\" + appGuid);

        public MainWindow()
        {
            if (!mutex.WaitOne(0, false))
            {
                var name = Assembly.GetExecutingAssembly().GetName().Name;
                MessageBox.Show("Another instance of " + name + " is already running.", name, MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }

            InitializeComponent();
            primaryViewModel = new DownloaderViewModel();
            DataContext = primaryViewModel;
            lvDownload.ItemsSource = primaryViewModel.DownloadItemsList;
            tvCategories.ItemsSource = primaryViewModel.CategoriesList;
        }

        private void menuExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void menuAbout_Click(object sender, RoutedEventArgs e)
        {
            var name = Assembly.GetExecutingAssembly().GetName().Name;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var description = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false).OfType<AssemblyDescriptionAttribute>().FirstOrDefault()?.Description;
            var copyright = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright;
            var funFact = "Total data transferred over lifetime: " + Math.Round((double)Settings.Default.BytesTransferredOverLifetime / (1024 * 1024 * 1024), 2) + " GB";
            MessageBox.Show(this, name + "\nVersion " + version + "\n\n" + description + "\n\n" + copyright + "\n\n" + funFact,
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            this.Title = "Quitting, please wait...";
            this.IsEnabled = false;
            e.Cancel = true;
        }
    }
}
