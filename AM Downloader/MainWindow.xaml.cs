using System.Linq;
using System.Reflection;
using System.Windows;

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
            tvCategories.ItemsSource = primaryViewModel.CategoriesList;
        }

        private void menuExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void menuAbout_Click(object sender, RoutedEventArgs e)
        {
            var name = Assembly.GetExecutingAssembly().GetName().Name;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var description = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false).OfType<AssemblyDescriptionAttribute>().FirstOrDefault()?.Description;
            var copyright = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright;

            MessageBox.Show(this,name + " " + version + "\n\n" + description + "\n\n" + copyright, "About",MessageBoxButton.OK,MessageBoxImage.Information);
        }
    }
}
