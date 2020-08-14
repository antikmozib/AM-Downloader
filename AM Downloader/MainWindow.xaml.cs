using System;
using System.Linq;
using System.Reflection;
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

        public MainWindow()
        {
            InitializeComponent(); 
            primaryViewModel = new DownloaderViewModel(this);
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

            MessageBox.Show(this, name + "\nVersion " + version + "\n\n" + description + "\n\n" + copyright, "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            this.Title = "Quitting, please wait...";
            this.IsEnabled = false;
            Settings.Default.Save();
            e.Cancel = true;
        }
    }
}
