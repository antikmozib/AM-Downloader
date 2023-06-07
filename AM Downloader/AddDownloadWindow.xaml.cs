// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using AMDownloader.ObjectModel.Serializable;
using AMDownloader.Properties;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Xml.Serialization;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for AddDownloadWindow.xaml
    /// </summary>
    ///
    public partial class AddDownloadWindow : Window
    {
        #region Native

        private const uint WS_EX_CONTEXTHELP = 0x00000400;
        private const uint WS_MINIMIZEBOX = 0x00020000;
        private const uint WS_MAXIMIZEBOX = 0x00010000;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_CONTEXTHELP = 0xF180;
        private const int WM_SETICON = 0x0080;
        private const int WS_EX_DLGMODALFRAME = 0x0001;

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, uint newStyle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            uint styles = GetWindowLong(hwnd, GWL_STYLE);

            styles &= 0xFFFFFFFF ^ (WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            SetWindowLong(hwnd, GWL_STYLE, styles);

            styles = GetWindowLong(hwnd, GWL_EXSTYLE);
            styles |= WS_EX_CONTEXTHELP;
            SetWindowLong(hwnd, GWL_EXSTYLE, styles);

            styles = GetWindowLong(hwnd, GWL_EXSTYLE);
            styles |= WS_EX_DLGMODALFRAME;
            SetWindowLong(hwnd, GWL_EXSTYLE, styles);

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_CONTEXTHELP)
                {
                    MessageBox.Show(
                        "You can use patterns to download multiple files from a single URL. For example, entering the following pattern" +
                        "\n\n" + "http://www.example.com/file[1:10].txt" +
                        "\n\n" + "will download the following ten files" +
                        "\n\n" + "file1.txt" + "\n" + "file2.txt" + "\n" + "..." + "\n\n" + "file10.txt",
                        "Help", MessageBoxButton.OK, MessageBoxImage.Information);
                    handled = true;
                }
                return IntPtr.Zero;
            });

            SendMessage(hwnd, WM_SETICON, new IntPtr(1), IntPtr.Zero);
            SendMessage(hwnd, WM_SETICON, IntPtr.Zero, IntPtr.Zero);
        }

        #endregion

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
            DialogResult = true;
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