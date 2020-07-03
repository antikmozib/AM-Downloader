using System.Net.Http;
using System.Windows;

namespace AM_Downloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DownloaderViewModel primaryViewModel = new DownloaderViewModel();
        private DownloaderModel.AddDownloadItemModel addItemViewModel;
        private HttpClient httpClient = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = primaryViewModel;
            lvDownload.ItemsSource = primaryViewModel.DownloadItemsList;
            primaryViewModel.DownloadItemsList.Add(new DownloaderModel.DownloaderItemModel(ref httpClient, @"https://download-installer.cdn.mozilla.net/pub/firefox/releases/77.0.1/win64/en-US/Firefox%20Setup%2077.0.1.exe", null, false));
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddDownloadWindow addDownloadWindow = new AddDownloadWindow();
            addItemViewModel = new DownloaderModel.AddDownloadItemModel(ref httpClient, ref primaryViewModel.DownloadItemsList);

            addDownloadWindow.DataContext = addItemViewModel;
            addDownloadWindow.Owner = this;

            addDownloadWindow.ShowDialog();
        }
    }
}
