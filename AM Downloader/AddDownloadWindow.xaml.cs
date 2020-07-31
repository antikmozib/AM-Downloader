using System.IO;
using System.Windows;
using System.Xml.Serialization;
using Ookii.Dialogs.Wpf;
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

            if (Directory.Exists(PATH_TO_SAVED_LOCATIONS_HISTORY))
            {
                XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistory));
                foreach (var file in Directory.GetFiles(PATH_TO_SAVED_LOCATIONS_HISTORY))
                {
                    try
                    {
                        var streamReader = new StreamReader(file);
                        var item = (SerializableDownloadPathHistory)writer.Deserialize(streamReader);
                        if (Directory.Exists(item.path) && !cboDestination.Items.Contains(item.path))
                        {
                            cboDestination.Items.Add(item.path);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            if (!cboDestination.Items.Contains(PATH_TO_DOWNLOADS_FOLDER))
                cboDestination.Items.Add(PATH_TO_DOWNLOADS_FOLDER);

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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!Directory.Exists(PATH_TO_SAVED_LOCATIONS_HISTORY))
            {
                try
                {
                    Directory.CreateDirectory(PATH_TO_SAVED_LOCATIONS_HISTORY);
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
                var model = new SerializableDownloadPathHistory();
                var streamWriter = new StreamWriter(Path.Combine(PATH_TO_SAVED_LOCATIONS_HISTORY, ++i + ".xml"));

                model.path = item.ToString();

                writer.Serialize(streamWriter, model);
                streamWriter.Close();
            }
        }
    }
}
