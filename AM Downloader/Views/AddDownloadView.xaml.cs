// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using AMDownloader.Models.Serializable;
using AMDownloader.Properties;
using AMDownloader.ViewModels;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for AddDownloadWindow.xaml
    /// </summary>
    public partial class AddDownloadView : Window
    {
        private CancellationTokenSource _monitorClipboardCts;
        private TaskCompletionSource _monitorClipboardTcs;

        private bool IsClipboardMonitorRunning => _monitorClipboardTcs != null && _monitorClipboardTcs.Task.Status != TaskStatus.RanToCompletion;

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
            // add the default %USERPROFILE%/Downloads folder
            DestinationComboBox.Items.Add(Common.Paths.UserDownloadsFolder);

            // restore all previous download folders
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

            // restore clipboard observer settings
            if (Settings.Default.MonitorClipboard)
            {
                MonitorClipboardCheckBox.IsChecked = true;
            }
            else
            {
                UrlTextBox.Text = AddDownloadViewModel.GenerateValidUrl(ClipboardObserver.GetText()) + Environment.NewLine;
            }

            // move cursor to the end of the TextBox
            if (UrlTextBox.Text.Length > 0)
            {
                UrlTextBox.Select(UrlTextBox.Text.Length - 1, 0);
            }
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
            WinForms.FolderBrowserDialog folderBrowser = new();

            if (Directory.Exists(DestinationComboBox.Text))
            {
                folderBrowser.SelectedPath = DestinationComboBox.Text;
            }
            else
            {
                folderBrowser.SelectedPath = Common.Paths.UserDownloadsFolder;
            }

            if (folderBrowser.ShowDialog() == WinForms.DialogResult.OK)
            {
                if (!DestinationComboBox.Items.Contains(folderBrowser.SelectedPath))
                {
                    DestinationComboBox.Items.Add(folderBrowser.SelectedPath);
                }

                DestinationComboBox.Text = folderBrowser.SelectedPath;
            }
        }

        private void UrlTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
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

        private async Task MonitorClipboardAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Log.Debug("Polling clipboard...");

                var textBlockUrls = string.Empty;
                var newUrls = string.Empty;
                var delay = Task.Delay(1000, ct);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    textBlockUrls = UrlTextBox.Text;
                });

                if (string.IsNullOrWhiteSpace(textBlockUrls))
                {
                    newUrls = string.Join(Environment.NewLine, AddDownloadViewModel.GenerateValidUrl(ClipboardObserver.GetText()));
                }
                else
                {
                    var existingUrls = textBlockUrls.Split(Environment.NewLine);
                    var incomingUrls = AddDownloadViewModel.GenerateValidUrl(ClipboardObserver.GetText())
                        .Split(Environment.NewLine);

                    foreach (var url in incomingUrls)
                    {
                        if (existingUrls.Contains(url))
                        {
                            continue;
                        }

                        newUrls += Environment.NewLine + url;
                    }
                }

                if (newUrls.Trim().Length > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UrlTextBox.Text = textBlockUrls.TrimEnd() + newUrls + Environment.NewLine;
                    });
                }

                // must await within try/catch otherwise an OperationCanceledException
                // will be thrown and the Task will exit abruptly without setting
                // the result of _monitorClipboardTcs
                try
                {
                    await delay;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _monitorClipboardTcs.SetResult();
        }

        private void MonitorClipboardCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Default.MonitorClipboard = true;

            if (IsClipboardMonitorRunning)
            {
                return;
            }

            _monitorClipboardTcs = new TaskCompletionSource();
            _monitorClipboardCts = new CancellationTokenSource();
            var ct = _monitorClipboardCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await MonitorClipboardAsync(ct);
                }
                catch
                {

                }

                _monitorClipboardCts.Dispose();

                Log.Debug("Stopped polling clipboard.");
            });
        }

        private void MonitorClipboardCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.Default.MonitorClipboard = false;

            if (!IsClipboardMonitorRunning)
            {
                return;
            }

            _monitorClipboardCts.Cancel();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (IsClipboardMonitorRunning)
            {
                _monitorClipboardCts.Cancel();
            }
        }
    }
}