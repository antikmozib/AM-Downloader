using System.IO;
using System.Windows;
using System.Xml.Serialization;
using static AMDownloader.SerializableModels;
using static AMDownloader.Common;
using AMDownloader.Properties;

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

            if (Settings.Default.LastSavedLocation.Trim().Length == 0)
            {
                Settings.Default.LastSavedLocation = ApplicationPaths.DownloadsFolder;
            }
            cboDestination.Items.Add(Settings.Default.LastSavedLocation);
            cboDestination.Text = Settings.Default.LastSavedLocation;

            if (Directory.Exists(ApplicationPaths.SavedLocationsHistory))
            {
                XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistory));
                StreamReader streamReader;

                foreach (var file in Directory.GetFiles(ApplicationPaths.SavedLocationsHistory))
                {
                    streamReader = new StreamReader(file);

                    try
                    {
                        var item = (SerializableDownloadPathHistory)writer.Deserialize(streamReader);
                        if (item.path.Trim().Length == 0) continue;

                        if (!cboDestination.Items.Contains(item.path))
                        {
                            cboDestination.Items.Add(item.path);
                        }
                    }
                    catch
                    {
                        streamReader.Close();
                        continue;
                    }

                    streamReader.Close();
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
            if (!Directory.Exists(ApplicationPaths.SavedLocationsHistory))
            {
                try
                {
                    Directory.CreateDirectory(ApplicationPaths.SavedLocationsHistory);
                }
                catch
                {
                    return;
                }
            }

            XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistory));
            int i = 0;

            foreach (var item in cboDestination.Items)
            {
                var path = item.ToString();

                if (path.Trim().Length == 0) continue;

                var model = new SerializableDownloadPathHistory();
                var streamWriter = new StreamWriter(Path.Combine(ApplicationPaths.SavedLocationsHistory, ++i + ".xml"));

                model.path = path;

                writer.Serialize(streamWriter, model);
                streamWriter.Close();
            }

            if (Settings.Default.RememberLastSavedLocation)
            {
                Settings.Default.LastSavedLocation = cboDestination.Text;
            }

            Settings.Default.Save();
        }
    }
}
