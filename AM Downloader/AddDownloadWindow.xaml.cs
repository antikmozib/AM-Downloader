// Copyright (C) 2020 Antik Mozib. All Rights Reserved.

using AMDownloader.Common;
using AMDownloader.ObjectModel.Serializable;
using AMDownloader.Properties;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;

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
                    cboDestination.Text = AppPaths.DownloadsFolder;
                }
            }
            else
            {
                cboDestination.Text = AppPaths.DownloadsFolder;
            }
            if (!cboDestination.Items.Contains(AppPaths.DownloadsFolder)) cboDestination.Items.Add(AppPaths.DownloadsFolder);

            if (Directory.Exists(AppPaths.LocalAppData))
            {
                try
                {
                    XmlSerializer writer = new XmlSerializer(typeof(SerializableDownloadPathHistoryList));
                    using (StreamReader streamReader = new StreamReader(AppPaths.SavedLocationsFile))
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
                dlg.SelectedPath = AppPaths.DownloadsFolder;
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
            if (File.Exists(AppPaths.SavedLocationsFile))
            {
                File.Delete(AppPaths.SavedLocationsFile);
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
                using (var streamWriter = new StreamWriter(AppPaths.SavedLocationsFile))
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

        internal void Preview(string preview)
        {
            var urls = (from url in preview.Split('\n').ToList<string>() where url.Trim().Length > 0 select url).ToList<string>();
            var previewViewModel = new PreviewViewModel("Preview URL patterns:", urls);
            var previewWindow = new PreviewWindow();
            previewWindow.DataContext = previewViewModel;
            previewWindow.Owner = this;
            previewWindow.ShowDialog();
        }
    }
}