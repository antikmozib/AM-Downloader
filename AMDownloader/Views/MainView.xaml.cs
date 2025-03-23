// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.Models.Serialization;
using AMDownloader.Properties;
using AMDownloader.ViewModels;
using Ookii.Dialogs.Wpf;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainView : Window
    {
        private bool _dataContextClosed = false;
        private ICollectionView _dataView = null;
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection? _lastDirection = null;

        public MainView()
        {
            InitializeComponent();

            DataContext = new MainViewModel(ShowWindow, ShowPrompt, NotifyUpdateAvailable, DataContext_Closing, DataContext_Closed);
        }

        private void Window_Initialized(object sender, EventArgs e)
        {
            // If running for the first time, center window.

            if (Settings.Default.FirstRun)
            {
                int desktopWidth = WinForms.Screen.AllScreens.FirstOrDefault().WorkingArea.Width;
                int desktopHeight = WinForms.Screen.AllScreens.FirstOrDefault().WorkingArea.Height;

                Left = desktopWidth / 2 - Width / 2;
                Top = desktopHeight / 2 - Height / 2;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore column sort direction.

            if (Settings.Default.SelectedColumnHeader != null)
            {
                // Get the column to sort.
                GridViewColumn gridViewColumn =
                    ((GridView)DownloadsListView.View).Columns.FirstOrDefault(
                        x => (string)x.Header == Settings.Default.SelectedColumnHeader);

                if (gridViewColumn != null)
                {
                    List<GridViewColumnHeader> headers = GetVisualChildren<GridViewColumnHeader>(DownloadsListView).ToList();
                    GridViewColumnHeader gridViewColumnHeader = null;

                    // Get the header of the column to sort.
                    foreach (GridViewColumnHeader header in headers)
                    {
                        if (header.Column == null || header.Column.Header == null) continue;

                        if ((header.Column.Header as string).Equals(gridViewColumn.Header as string))
                        {
                            gridViewColumnHeader = header;
                            break;
                        }
                    }

                    Sort(gridViewColumnHeader, Settings.Default.SelectedColumnHeaderDirection);
                }
            }

            // Restore column order and widths.

            if (File.Exists(Constants.UIColumnOrderFile))
            {
                try
                {
                    var restoreCols = Common.Deserialize<SerializingUIColumnList>(Constants.UIColumnOrderFile);
                    var gridCols = ((GridView)DownloadsListView.View).Columns;

                    for (int i = 0; i < restoreCols.Objects.Count; i++)
                    {
                        var restoreCol = restoreCols.Objects[i];

                        for (int j = 0; j < gridCols.Count; j++)
                        {
                            var gridCol = gridCols[j];

                            if (gridCol.Header.ToString() == restoreCol.Name)
                            {
                                gridCol.Width = restoreCol.Width;

                                if (gridCols.IndexOf(gridCol) != restoreCol.Index)
                                {
                                    var swapCol = gridCols[restoreCol.Index];
                                    gridCols.Move(j, restoreCol.Index);
                                    gridCols.Move(gridCols.IndexOf(swapCol), j);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                }
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is ICloseable context)
            {
                if (!_dataContextClosed)
                {
                    e.Cancel = true;
                    context.Close();

                    return;
                }
            }

            // Save column order and widths.

            var columnOrderList = new SerializingUIColumnList();

            foreach (var column in ((GridView)DownloadsListView.View).Columns)
            {
                SerializingUIColumn serializableUIColumnOrder = new()
                {
                    Index = ((GridView)DownloadsListView.View).Columns.IndexOf(column),
                    Name = column.Header.ToString(),
                    Width = column.Width
                };
                columnOrderList.Objects.Add(serializableUIColumnOrder);
            }

            try
            {
                Directory.CreateDirectory(Constants.LocalAppDataFolder);
                Common.Serialize(columnOrderList, Constants.UIColumnOrderFile);
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        }

        public void DataContext_Closing(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => IsEnabled = false);
        }

        public void DataContext_Closed(object sender, EventArgs e)
        {
            _dataContextClosed = true;

            Dispatcher.Invoke(() => Close());
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            var cultureInfo = CultureInfo.CurrentCulture;
            var name = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product;
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var copyright = Assembly
                .GetExecutingAssembly()
                .GetCustomAttributes(typeof(AssemblyCopyrightAttribute), true)
                .OfType<AssemblyCopyrightAttribute>()
                .FirstOrDefault()
                ?.Copyright;
            var website = (string)Application.Current.FindResource("productUrl");
            var websiteDisplay = $"<a href=\"{website}\">{website}</a>";

            var aboutDialog = new TaskDialog
            {
                CenterParent = true,
                EnableHyperlinks = true,
                MainIcon = TaskDialogIcon.Information,
                WindowTitle = "About",
                MainInstruction = name,
                Content = $"Version {version}\n\n{copyright}\n{websiteDisplay}"
            };

            aboutDialog.HyperlinkClicked += (s, e) =>
            {
                Process.Start("explorer.exe", e.Href);
            };

            aboutDialog.Buttons.Add(new TaskDialogButton(ButtonType.Ok));
            aboutDialog.ShowDialog(this);
        }

        private void CategoriesList_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Settings.Default.LastSelectedCatagory))
                {
                    (CategoriesList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem).IsSelected = true;
                }
                else
                {
                    (CategoriesList.ItemContainerGenerator.ContainerFromIndex(
                        (int)(Category)Enum.Parse(typeof(Category), Settings.Default.LastSelectedCatagory)) as ListBoxItem).IsSelected = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        }

        private void DownloadsListView_HeaderClick(object sender, RoutedEventArgs e)
        {
            // Default sorting direction for a previously unsorted column.
            ListSortDirection? direction = ListSortDirection.Descending;

            var headerClicked = e.OriginalSource as GridViewColumnHeader;

            if (headerClicked == null || headerClicked.Role == GridViewColumnHeaderRole.Padding)
            {
                return;
            }

            // Change the sorting direction if the column is already sorted.
            if (headerClicked == _lastHeaderClicked)
            {
                if (_lastDirection == ListSortDirection.Descending)
                {
                    direction = ListSortDirection.Ascending;
                }
                else if (_lastDirection == ListSortDirection.Ascending)
                {
                    direction = null;
                }
                else
                {
                    direction = ListSortDirection.Descending;
                }
            }

            Sort(headerClicked, direction);
        }

        private void Sort(GridViewColumnHeader columnHeader, ListSortDirection? direction)
        {
            if (_dataView == null)
            {
                _dataView = CollectionViewSource.GetDefaultView(DownloadsListView.ItemsSource);
            }

            var columnBinding = columnHeader.Column.DisplayMemberBinding as Binding;
            var columnToSort = columnBinding?.Path.Path ?? columnHeader.Column.Header as string;

            // Link the column header string value to the property of the binded object; this is only needed for those
            // columns whose header names are different from the property names.
            switch (columnToSort)
            {
                case "Type":
                    columnToSort = "Extension";
                    break;

                case "Downloaded":
                    columnToSort = "BytesDownloaded";
                    break;

                case "Size":
                    columnToSort = "TotalBytesToDownload";
                    break;

                case "ETA":
                    columnToSort = "TimeRemaining";
                    break;

                case "Location":
                    columnToSort = "Destination";
                    break;

                case "URL":
                    columnToSort = "Url";
                    break;

                case "Added":
                    columnToSort = "CreatedOn";
                    break;

                case "Completed":
                    columnToSort = "CompletedOn";
                    break;

                case "HTTP Status":
                    columnToSort = "StatusCode";
                    break;
            }

            _dataView.SortDescriptions.Clear();

            if (direction != null)
            {
                SortDescription sd = new(columnToSort, direction ?? ListSortDirection.Descending);
                _dataView.SortDescriptions.Add(sd);

                Settings.Default.SelectedColumnHeader = columnHeader.Column.Header.ToString();
            }
            else
            {
                // If we're unsorting, store SelectedColumnHeader as null so that sorting isn't applied incorrectly when
                // the app is relaunched.
                Settings.Default.SelectedColumnHeader = null;
            }

            // Remove the arrow from the last sorted column.
            if (_lastHeaderClicked != null)
            {
                _lastHeaderClicked.Column.HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate;
            }

            // Apply the arrow to the newly sorted column.
            if (direction == ListSortDirection.Descending)
            {
                columnHeader.Column.HeaderTemplate = Resources["HeaderTemplateArrowDown"] as DataTemplate;
            }
            else if (direction == ListSortDirection.Ascending)
            {
                columnHeader.Column.HeaderTemplate = Resources["HeaderTemplateArrowUp"] as DataTemplate;
            }
            else
            {
                columnHeader.Column.HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate;
            }

            _dataView.Refresh();
            _lastHeaderClicked = columnHeader;
            _lastDirection = direction;

            Settings.Default.SelectedColumnHeaderDirection = direction ?? ListSortDirection.Descending;
        }

        public bool? ShowPrompt(
            string promptText,
            string caption,
            PromptButton button,
            PromptIcon icon,
            bool defaultResult)
        {
            bool? result = null;

            Dispatcher.Invoke(() => result = Prompt.Show(promptText, caption, button, icon, defaultResult));

            return result;
        }

        public bool? ShowWindow(object viewModel)
        {
            Window window = null;
            bool? result = null;

            // ViewModels must be assigned and the window must be initialized and shown explicitly from the main thread.
            Dispatcher.Invoke(() =>
            {
                if (viewModel is AddDownloadViewModel)
                {
                    window = new AddDownloadView();
                }
                else if (viewModel is SettingsViewModel)
                {
                    window = new SettingsView();
                }
                else if (viewModel is ListViewerViewModel)
                {
                    window = new ListViewerView();
                }

                if (window != null)
                {
                    window.DataContext = viewModel;
                    window.Owner = this;
                }

                result = window?.ShowDialog();
            });

            return result;
        }

        /// <summary>
        /// Displays information about the latest available update.
        /// </summary>
        /// <param name="latestUpdateInfo">Information provided by the update API about the latest available update.</param>
        /// <param name="showReminderButton">If <see langword="true"/>, a button to stop reminding about updates will be 
        /// shown.</param>
        private void NotifyUpdateAvailable(Mozib.AppUpdater.Models.UpdateInfo latestUpdateInfo, bool showReminderButton)
        {
            TaskDialog taskDialog = new()
            {
                WindowTitle = "Update",
                CenterParent = true,
                MainInstruction = "An update is available.",
                Content = $"Latest version: {latestUpdateInfo.Version}" +
                    $"\nCurrent version: {Assembly.GetExecutingAssembly().GetName().Version}" +
                    $"\n\n<a href=\"{latestUpdateInfo.UpdateInfoUrl}\">More information</a>",
                MainIcon = TaskDialogIcon.Information,
                EnableHyperlinks = true
            };

            TaskDialogButton downloadButton = new("Download")
            {
                Default = true
            };

            TaskDialogButton noReminderButton = new()
            {
                Text = "Don't Remind Again"
            };

            TaskDialogButton result = null;

            taskDialog.HyperlinkClicked += (s, e) =>
            {
                Process.Start("explorer.exe", e.Href);
            };

            taskDialog.Buttons.Add(downloadButton);
            taskDialog.Buttons.Add(new TaskDialogButton(ButtonType.Cancel));
            if (showReminderButton)
            {
                taskDialog.Buttons.Add(noReminderButton);
            }

            Dispatcher.Invoke(() => result = taskDialog.ShowDialog(this));

            if (result == downloadButton)
            {
                Process.Start("explorer.exe", latestUpdateInfo.FileUrl);
            }
            else if (result == noReminderButton)
            {
                Settings.Default.AutoCheckForUpdates = false;
            }
        }

        private static IEnumerable<T> GetVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                {
                    yield return t;
                }

                foreach (var descendant in GetVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}