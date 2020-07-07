using System.IO;
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

        public MainWindow()
        {
            InitializeComponent();

            DataContext = primaryViewModel;
            lvDownload.ItemsSource = primaryViewModel.DownloadItemsList;

            /*DownloaderModel.AddDownloadItemModel addDownloadItem = new DownloaderModel.AddDownloadItemModel(
                url: test,
                destination: @"c:\users\antik\desktop\" + Path.GetFileName(test)
                );

            primaryViewModel.DownloadItemsList.Add(new DownloaderModel.DownloaderItemModel(ref DownloaderViewModel.httpClient, addDownloadItem));*/
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddDownloadWindow addDownloadWindow = new AddDownloadWindow();
            DownloaderModels.AddDownloaderItemModel addItemModel = new DownloaderModels.AddDownloaderItemModel(ref primaryViewModel.httpClient, ref primaryViewModel.DownloadItemsList);
            addItemModel.Urls = @"https://download-installer.cdn.mozilla.net/pub/firefox/releases/77.0.1/win64/en-US/Firefox%20Setup%2077.0.1.exe" + '\n' + @"https://download3.operacdn.com/pub/opera/desktop/69.0.3686.49/win/Opera_69.0.3686.49_Setup_x64.exe";
            addDownloadWindow.DataContext = addItemModel;         
            addDownloadWindow.Owner = this;
            addDownloadWindow.ShowDialog();
        }
    }
}
