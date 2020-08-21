using MahApps.Metro.Controls;
using System.Windows;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class OptionsWindow : MetroWindow
    {
        private OptionsViewModel optionsViewModel = new OptionsViewModel();

        public OptionsWindow()
        {
            InitializeComponent();
            DataContext = optionsViewModel;
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
