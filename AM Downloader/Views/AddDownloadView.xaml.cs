// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.ViewModels;
using Ookii.Dialogs.Wpf;
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
        private bool _isContextClosed = false;
        private CancellationTokenSource _monitorClipboardCts;
        private TaskCompletionSource _monitorClipboardTcs;

        private bool IsClipboardMonitorRunning =>
            _monitorClipboardTcs != null
            && _monitorClipboardTcs.Task.Status != TaskStatus.RanToCompletion;

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

            Helpers.Native.User32.SetWindowPos(
                hwnd: hwnd,
                hwndInsertAfter: IntPtr.Zero,
                x: 0,
                y: 0,
                width: 0,
                height: 0,
                flags: Helpers.Native.User32.SWP_NOMOVE
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
                    TaskDialog helpDialog = new()
                    {
                        WindowTitle = "Help",
                        CenterParent = true,
                        MainIcon = TaskDialogIcon.Information,
                        MainInstruction = (string)Application.Current.FindResource("addDownloadHelpTitle"),
                        Content = (string)Application.Current.FindResource("addDownloadHelpText")
                    };
                    helpDialog.Buttons.Add(new TaskDialogButton(ButtonType.Ok));
                    helpDialog.ShowDialog(this);

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
            var context = (AddDownloadViewModel)DataContext;
            context.ShowList = ShowList;
            context.ShowPrompt = ShowPrompt;
            context.ShowFolderBrowser = ShowFolderBrowser;
            context.Closed += Context_Closed;

            // add any urls from the clipboard
            if (!IsClipboardMonitorRunning)
            {
                var clipUrls = AddDownloadViewModel.GenerateValidUrl(ClipboardObserver.GetText());
                UrlTextBox.Text = !string.IsNullOrWhiteSpace(clipUrls) ? clipUrls + Environment.NewLine : string.Empty;
            }

            // move cursor to the end of UrlTextBox
            if (UrlTextBox.Text.Length > 0)
            {
                UrlTextBox.Select(UrlTextBox.Text.Length - 1, 0);
            }
            UrlTextBox.Focus();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (IsClipboardMonitorRunning)
            {
                _monitorClipboardCts.Cancel();
            }

            if (DataContext is ICloseable context && !_isContextClosed)
            {
                e.Cancel = true;
                context.Close();
            }
        }

        private void Context_Closed(object sender, EventArgs e)
        {
            _isContextClosed = true;
            Dispatcher.BeginInvoke(() => Close());
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            DestinationComboBox.Focus();
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

            if (fontSize < FontSize)
            {
                fontSize = FontSize;
            }

            if (fontSize > (3 * FontSize))
            {
                fontSize = 3 * FontSize;
            }

            UrlTextBox.FontSize = fontSize;
        }

        private void MonitorClipboardCheckBox_Checked(object sender, RoutedEventArgs e)
        {
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
            if (!IsClipboardMonitorRunning)
            {
                return;
            }

            _monitorClipboardCts.Cancel();
        }

        private async Task MonitorClipboardAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Log.Debug("Polling clipboard...");

                    var textBlockUrls = string.Empty;
                    var newUrls = string.Empty;
                    var delay = Task.Delay(2000, ct);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        textBlockUrls = UrlTextBox.Text;
                    });

                    if (string.IsNullOrWhiteSpace(textBlockUrls))
                    {
                        // UrlTextBox is empty
                        newUrls = string.Join(Environment.NewLine, AddDownloadViewModel
                            .GenerateValidUrl(ClipboardObserver.GetText()));
                    }
                    else
                    {
                        // UrlTextBox isn't empty

                        var existingUrls = textBlockUrls
                            .Split(Environment.NewLine)
                            .Select(o => o.ToLower());
                        var incomingUrls = AddDownloadViewModel
                            .GenerateValidUrl(ClipboardObserver.GetText())
                            .Split(Environment.NewLine);

                        foreach (var url in incomingUrls)
                        {
                            if (existingUrls.Contains(url.ToLower()))
                            {
                                continue;
                            }

                            newUrls += Environment.NewLine + url;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(newUrls))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UrlTextBox.Text = textBlockUrls.TrimEnd() + newUrls + Environment.NewLine;
                        });
                    }

                    await delay;
                }
            }
            catch (OperationCanceledException)
            {

            }

            _monitorClipboardTcs.SetResult();
        }

        public bool? ShowList(object viewModel)
        {
            Window window = null;
            Dispatcher.Invoke(() =>
            {
                window = new ListViewerView
                {
                    DataContext = viewModel,
                    Owner = this
                };
                window.ShowDialog();
            });
            return true;
        }

        public bool? ShowPrompt(
            string promptText,
            string caption,
            PromptButton button,
            PromptIcon icon,
            bool defaultResult = true)
        {
            bool? result = null;

            Dispatcher.Invoke(() => result = Prompt.Show(promptText, caption, button, icon, defaultResult));

            return result;
        }

        private (bool, string) ShowFolderBrowser()
        {
            WinForms.FolderBrowserDialog folderBrowser = new();
            string openWithFolder;

            if (Directory.Exists(DestinationComboBox.Text))
            {
                openWithFolder = DestinationComboBox.Text;
            }
            else
            {
                openWithFolder = Common.Paths.UserDownloadsFolder;
            }

            // ensure there's a trailing slash so that FolderBrowserDialog opens inside SelectedPath
            if (openWithFolder.LastIndexOf(Path.DirectorySeparatorChar) != openWithFolder.Length - 1)
            {
                openWithFolder += Path.DirectorySeparatorChar;
            }

            folderBrowser.SelectedPath = openWithFolder;

            if (folderBrowser.ShowDialog() == WinForms.DialogResult.OK)
            {
                return (true, folderBrowser.SelectedPath);
            }

            return (false, string.Empty);
        }
    }
}