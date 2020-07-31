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

            if (Directory.Exists(PATH_TO_SAVED_LOCATIONS_HISTORY))
            {
                XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistory));
                foreach (var file in Directory.GetFiles(PATH_TO_SAVED_LOCATIONS_HISTORY))
                {
                    try
                    {
                        var streamReader = new StreamReader(file);
                        var item = (SerializableDownloadPathHistory)writer.Deserialize(streamReader);
                        if (Directory.Exists(item.path))
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

            if (!cboDestination.Items.Contains(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
                cboDestination.Items.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
             
            if (cboDestination.Items.Count > 0) cboDestination.SelectedIndex = cboDestination.Items.Count - 1;

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
            try
            {
                if (!Directory.Exists(PATH_TO_SAVED_LOCATIONS_HISTORY))
                {
                    Directory.CreateDirectory(PATH_TO_SAVED_LOCATIONS_HISTORY);
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
            catch
            {

            }
        }
    }
}
