using System;
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
                        if (Directory.Exists(item.path) && !cboDestination.Items.Contains(item.path))
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

            if (!cboDestination.Items.Contains(ApplicationPaths.DownloadsFolder))
                cboDestination.Items.Add(ApplicationPaths.DownloadsFolder);

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
                var model = new SerializableDownloadPathHistory();
                var streamWriter = new StreamWriter(Path.Combine(ApplicationPaths.SavedLocationsHistory, ++i + ".xml"));

                model.path = item.ToString();

                writer.Serialize(streamWriter, model);
                streamWriter.Close();
            }
        }
    }
}
