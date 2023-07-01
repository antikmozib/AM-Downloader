// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models.Serializable;
using AMDownloader.Properties;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for AddDownloadWindow.xaml
    /// </summary>
    public partial class AddDownloadView : Window
    {
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            uint styles = Helpers.Native.User32.GetWindowLong(hwnd, Helpers.Native.User32.GWL_STYLE);

            styles &= 0xFFFFFFFF ^ (Helpers.Native.User32.WS_MINIMIZEBOX | Helpers.Native.User32.WS_MAXIMIZEBOX);
            Helpers.Native.User32.SetWindowLong(hwnd, Helpers.Native.User32.GWL_STYLE, styles);

            styles = Helpers.Native.User32.GetWindowLong(hwnd, Helpers.Native.User32.GWL_EXSTYLE);
            styles |= Helpers.Native.User32.WS_EX_CONTEXTHELP;
            Helpers.Native.User32.SetWindowLong(hwnd, Helpers.Native.User32.GWL_EXSTYLE, styles);

            styles = Helpers.Native.User32.GetWindowLong(hwnd, Helpers.Native.User32.GWL_EXSTYLE);
            styles |= Helpers.Native.User32.WS_EX_DLGMODALFRAME;
            Helpers.Native.User32.SetWindowLong(hwnd, Helpers.Native.User32.GWL_EXSTYLE, styles);

            Helpers.Native.User32.SetWindowPos(hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                Helpers.Native.User32.SWP_NOMOVE
                    | Helpers.Native.User32.SWP_NOSIZE
                    | Helpers.Native.User32.SWP_NOZORDER
                    | Helpers.Native.User32.SWP_FRAMECHANGED);

            ((HwndSource)PresentationSource.FromVisual(this)).AddHook((IntPtr hwnd,
                int msg,
                IntPtr wParam,
                IntPtr lParam,
                ref bool handled) =>
            {
                if (msg == Helpers.Native.User32.WM_SYSCOMMAND
                    && ((int)wParam & 0xFFF0) == Helpers.Native.User32.SC_CONTEXTHELP)
                {
                    MessageBox.Show(
                        "Patterns can be applied to download multiple files from a single URL."
                        + "\nFor example, entering the following pattern:"
                        + "\n\n\thttp://www.example.com/file[1:10].png"
                        + "\n\nwill download the following ten files:"
                        + "\n\n\tfile1.png\n\tfile2.png\n\t...\n\n\tfile10.png",
                        "Help", MessageBoxButton.OK, MessageBoxImage.Information);
                    handled = true;
                }

                return IntPtr.Zero;
            });

            Helpers.Native.User32.SendMessage(hwnd, Helpers.Native.User32.WM_SETICON, new IntPtr(1), IntPtr.Zero);
            Helpers.Native.User32.SendMessage(hwnd, Helpers.Native.User32.WM_SETICON, IntPtr.Zero, IntPtr.Zero);
        }

        public AddDownloadView()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Settings.Default.RememberLastDownloadLocation)
            {
                if (Settings.Default.LastDownloadLocation.Trim().Length > 0)
                {
                    DestinationComboBox.Items.Add(Settings.Default.LastDownloadLocation);
                    DestinationComboBox.Text = Settings.Default.LastDownloadLocation;
                }
                else
                {
                    DestinationComboBox.Text = Common.Paths.UserDownloadsFolder;
                }
            }
            else
            {
                DestinationComboBox.Text = Common.Paths.UserDownloadsFolder;
            }

            if (!DestinationComboBox.Items.Contains(Common.Paths.UserDownloadsFolder))
            {
                DestinationComboBox.Items.Add(Common.Paths.UserDownloadsFolder);
            }

            if (File.Exists(Common.Paths.SavedLocationsFile))
            {
                try
                {
                    var list = Common.Functions.Deserialize<SerializableDownloadPathHistoryList>(Common.Paths.SavedLocationsFile);

                    foreach (var item in list.Objects)
                    {
                        if (item.FolderPath.Trim().Length > 0 && !DestinationComboBox.Items.Contains(item.FolderPath))
                        {
                            DestinationComboBox.Items.Add(item.FolderPath);
                        }
                    }
                }
                catch
                {

                }
            }

            UrlTextBox.SelectAll();
            UrlTextBox.Focus();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // ensure the selected path is valid and accessible
            try
            {
                Directory.CreateDirectory(DestinationComboBox.Text);

                var f = File.Create(Path.Combine(DestinationComboBox.Text, DateTime.Now.ToFileTimeUtc().ToString()));

                f.Close();
                File.Delete(f.Name);
            }
            catch
            {
                MessageBox.Show("The selected location is inaccessible.", "Add", MessageBoxButton.OK, MessageBoxImage.Error);
                DestinationComboBox.Focus();
                return;
            }

            if (Settings.Default.RememberLastDownloadLocation)
            {
                Settings.Default.LastDownloadLocation = DestinationComboBox.Text;
            }

            var list = new SerializableDownloadPathHistoryList();

            foreach (var item in DestinationComboBox.Items)
            {
                var path = item.ToString();

                if (path.Trim().Length == 0)
                {
                    continue;
                }

                var model = new SerializableDownloadPathHistory
                {
                    FolderPath = path
                };

                list.Objects.Add(model);
            }

            try
            {
                Common.Functions.Serialize(list, Common.Paths.SavedLocationsFile);
            }
            catch
            {

            }

            DialogResult = true;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();

            if (Directory.Exists(DestinationComboBox.Text))
            {
                dlg.SelectedPath = DestinationComboBox.Text;
            }
            else
            {
                dlg.SelectedPath = Common.Paths.UserDownloadsFolder;
            }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!DestinationComboBox.Items.Contains(dlg.SelectedPath))
                {
                    DestinationComboBox.Items.Add(dlg.SelectedPath);
                }

                DestinationComboBox.Text = dlg.SelectedPath;
            }
        }

        private void UrlTextBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            e.Handled = true;

            var fontSize = UrlTextBox.FontSize;

            if (e.Delta > 0)
            {
                ++fontSize;
            }
            else
            {
                --fontSize;
            }

            if (fontSize < this.FontSize)
            {
                fontSize = this.FontSize;
            }

            if (fontSize > (3 * this.FontSize))
            {
                fontSize = 3 * this.FontSize;
            }

            UrlTextBox.FontSize = fontSize;
        }
    }
}