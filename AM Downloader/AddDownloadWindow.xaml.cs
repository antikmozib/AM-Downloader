using System.Windows;

namespace AM_Downloader
{
    /// <summary>
    /// Interaction logic for AddDownloadWindow.xaml
    /// </summary>
    public partial class AddDownloadWindow : Window
    {
        public AddDownloadWindow()
        {
            InitializeComponent();
            txtUrl.Focus();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
