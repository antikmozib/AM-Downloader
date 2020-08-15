using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;
using AMDownloader.Properties;
using static AMDownloader.SerializableModels;
using static AMDownloader.Common;

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
            if (Settings.Default.RememberLastSavedLocation)
            {
                if (Settings.Default.LastSavedLocation.Trim().Length > 0)
                {
                    cboDestination.Items.Add(Settings.Default.LastSavedLocation);
                    cboDestination.Text = Settings.Default.LastSavedLocation;
                }
                else
                {
                    cboDestination.Text = ApplicationPaths.DownloadsFolder;
                }
            }
            else
            {
                cboDestination.Text = ApplicationPaths.DownloadsFolder;
            }
            if (!cboDestination.Items.Contains(ApplicationPaths.DownloadsFolder)) cboDestination.Items.Add(ApplicationPaths.DownloadsFolder);

            if (Directory.Exists(ApplicationPaths.LocalAppData))
            {
                try
                {
                    XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistoryList));
                    using (StreamReader streamReader = new StreamReader(ApplicationPaths.SavedLocationsFile))
                    {
                        SerializableDownloadPathHistoryList list;
                        list = (SerializableDownloadPathHistoryList)writer.Deserialize(streamReader);
                        foreach (var item in list.Objects)
                        {
                            if (item.path.Trim().Length > 0 && !cboDestination.Items.Contains(item.path))
                            {
                                cboDestination.Items.Add(item.path);
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            txtUrl.Focus();
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();

            if (Directory.Exists(cboDestination.Text))
            {
                dlg.SelectedPath = cboDestination.Text;
            }
            else
            {
                dlg.SelectedPath = ApplicationPaths.DownloadsFolder;
            }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!cboDestination.Items.Contains(dlg.SelectedPath))
                    cboDestination.Items.Add(dlg.SelectedPath);
                cboDestination.Text = dlg.SelectedPath;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (File.Exists(ApplicationPaths.SavedLocationsFile))
            {
                File.Delete(ApplicationPaths.SavedLocationsFile);
            }
            var list = new SerializableDownloadPathHistoryList();
            foreach (var item in cboDestination.Items)
            {
                var path = item.ToString();
                if (path.Trim().Length == 0) continue;
                var model = new SerializableDownloadPathHistory();
                model.path = path;
                list.Objects.Add(model);
            }
            try
            {
                using (var streamWriter = new StreamWriter(ApplicationPaths.SavedLocationsFile))
                {
                    XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistoryList));
                    writer.Serialize(streamWriter, list);
                }
            }
            catch (IOException)
            {
                return;
            }

            if (Settings.Default.RememberLastSavedLocation)
            {
                Settings.Default.LastSavedLocation = cboDestination.Text;
            }

            Settings.Default.Save();
        }

        private void txtUrl_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }
            e.Handled = true;
            var fontSize = txtUrl.FontSize;
            if (e.Delta > 0)
            {
                ++fontSize;
            }
            else
            {
                --fontSize;
            }
            if (fontSize < this.FontSize) fontSize = this.FontSize;
            if (fontSize > (3 * this.FontSize)) fontSize = 3 * this.FontSize;
            txtUrl.FontSize = fontSize;
        }
    }
}
