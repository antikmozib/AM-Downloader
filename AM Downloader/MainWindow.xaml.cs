using System.Windows;
using System.Windows.Input;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DownloaderViewModel primaryViewModel = new DownloaderViewModel();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = primaryViewModel;
            lvDownload.ItemsSource = primaryViewModel.DownloadItemsList;
        }


    }
}
