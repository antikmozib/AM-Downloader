using System;
using System.Windows;
using Ookii.Dialogs.Wpf;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for AddDownloadWindow.xaml
    /// </summary>
    /// 
    public partial class AddDownloadWindow : Window
    {
        public AddDownloadWindow()
        {
            InitializeComponent();
            cboDestination.Items.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            txtUrl.Focus();
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new VistaFolderBrowserDialog();
            if ((bool)folderPicker.ShowDialog(this))
            {
                cboDestination.Items.Add(folderPicker.SelectedPath);
                cboDestination.SelectedIndex = cboDestination.Items.Count - 1;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
