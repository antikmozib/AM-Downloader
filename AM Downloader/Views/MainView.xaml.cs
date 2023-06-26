// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using AMDownloader.Models;
using AMDownloader.Models.Serializable;
using AMDownloader.Properties;
using AMDownloader.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainView : Window
    {
        private ICollectionView _dataView = null;
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection? _lastDirection = null;

        public MainView()
        {
            InitializeComponent();
        }

        private void Window_Initialized(object sender, EventArgs e)
        {
            // if running for the 1st time, center window

            if (Settings.Default.FirstRun)
            {
                int desktopWidth = WinForms.Screen.AllScreens.FirstOrDefault().WorkingArea.Width;
                int desktopHeight = WinForms.Screen.AllScreens.FirstOrDefault().WorkingArea.Height;

                this.Left = desktopWidth / 2 - this.Width / 2;
                this.Top = desktopHeight / 2 - this.Height / 2;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // restore column sort direction

            if (Settings.Default.SelectedColumnHeader != null)
            {
                // get the column to sort
                GridViewColumn gridViewColumn =
                    ((GridView)DownloadsListView.View).Columns.FirstOrDefault(
                        x => (string)x.Header == Settings.Default.SelectedColumnHeader);

                if (gridViewColumn != null)
                {
                    List<GridViewColumnHeader> headers = GetVisualChildren<GridViewColumnHeader>(DownloadsListView).ToList();
                    GridViewColumnHeader gridViewColumnHeader = null;

                    // get the header of the column to sort
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

            // restore column order and widths

            if (File.Exists(Paths.UIColumnOrderFile))
            {
                try
                {
                    var restoreCols = Functions.Deserialize<SerializableUIColumnList>(Paths.UIColumnOrderFile);
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
                catch
                {

                }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this.IsEnabled = false;
            var cancel = false;

            if (DataContext is IClosing context)
            {
                cancel = !context.OnClosing();
            }

            if (cancel)
            {
                e.Cancel = true;
                this.IsEnabled = true;
            }
            else
            {
                // save column order and widths

                var columnOrderList = new SerializableUIColumnList();

                foreach (var column in ((GridView)DownloadsListView.View).Columns)
                {
                    SerializableUIColumn serializableUIColumnOrder = new()
                    {
                        Index = ((GridView)DownloadsListView.View).Columns.IndexOf(column),
                        Name = column.Header.ToString(),
                        Width = column.Width
                    };
                    columnOrderList.Objects.Add(serializableUIColumnOrder);
                }

                try
                {
                    Directory.CreateDirectory(Paths.LocalAppDataFolder);
                    Functions.Serialize(columnOrderList, Paths.UIColumnOrderFile);
                }
                catch
                {

                }
            }
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AboutMenu_Click(object sender, RoutedEventArgs e)
        {
            var cultureInfo = CultureInfo.CurrentCulture;
            var name = Assembly.GetExecutingAssembly().GetName().Name;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var copyright = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(
                AssemblyCopyrightAttribute), true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright;
            var website = "https://mozib.io/amdownloader";
            var totalDownloaded = Math.Round(Settings.Default.BytesTransferredOverLifetime
                / (double)Constants.ByteConstants.MEGABYTE);

            MessageBox.Show(
                $"{name}\nVersion {version}\n\n{copyright}\n{website}"
                + $"\n\nTotal downloaded since installation: {totalDownloaded.ToString("n0", cultureInfo)} MB"
                + $"\nNumber of times launched: {Settings.Default.LaunchCount}",
                "About",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CategoriesList_Loaded(object sender, RoutedEventArgs e)
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

        private void DownloadsListView_HeaderClick(object sender, RoutedEventArgs e)
        {
            // default sorting direction for a previously unsorted column
            ListSortDirection? direction = ListSortDirection.Descending;

            var headerClicked = e.OriginalSource as GridViewColumnHeader;

            if (headerClicked == null || headerClicked.Role == GridViewColumnHeaderRole.Padding)
            {
                return;
            }

            // change the sorting direction if the column is already sorted
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

            // link the column header string value to the prop of the binded obj;
            // this is only needed for those columns whose header names are
            // different from the prop names
            switch (columnToSort.ToLower())
            {
                case "type":
                    columnToSort = "Extension";
                    break;

                case "downloaded":
                    columnToSort = "BytesDownloaded";
                    break;

                case "size":
                    columnToSort = "TotalBytesToDownload";
                    break;

                case "eta":
                    columnToSort = "TimeRemaining";
                    break;

                case "location":
                    columnToSort = "Destination";
                    break;

                case "url":
                    columnToSort = "Url";
                    break;

                case "created":
                    columnToSort = "DateCreated";
                    break;

                case "http status":
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
                // if we're unsorting, store the selected column header as null
                // so that sorting isn't applied incorrectly when the app is relaunched
                Settings.Default.SelectedColumnHeader = null;
            }

            // remove the arrow from the previously sorted column
            if (_lastHeaderClicked != null)
            {
                _lastHeaderClicked.Column.HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate;
            }

            // apply the arrow to the newly sorted column
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

        internal bool? ShowPrompt(
            string promptText,
            string caption,
            PromptButton button,
            PromptIcon icon,
            bool defaultResult = true)
        {
            var messageBoxButton = button switch
            {
                PromptButton.OK => MessageBoxButton.OK,
                PromptButton.OKCancel => MessageBoxButton.OKCancel,
                PromptButton.YesNo => MessageBoxButton.YesNo,
                PromptButton.YesNoCancel => MessageBoxButton.YesNoCancel,
                _ => throw new ArgumentOutOfRangeException(nameof(button))
            };
            var messageBoxImage = icon switch
            {
                PromptIcon.None => MessageBoxImage.None,
                PromptIcon.Error => MessageBoxImage.Error,
                PromptIcon.Question => MessageBoxImage.Question,
                PromptIcon.Exclamation => MessageBoxImage.Exclamation,
                PromptIcon.Warning => MessageBoxImage.Warning,
                PromptIcon.Asterisk => MessageBoxImage.Asterisk,
                PromptIcon.Information => MessageBoxImage.Information,
                _ => throw new ArgumentOutOfRangeException(nameof(icon))
            };
            var messageBoxDefaultResult = defaultResult switch
            {
                true => messageBoxButton == MessageBoxButton.OK || messageBoxButton == MessageBoxButton.OKCancel
                    ? MessageBoxResult.OK
                    : MessageBoxResult.Yes,
                false => messageBoxButton == MessageBoxButton.OKCancel
                    ? MessageBoxResult.Cancel
                    : MessageBoxResult.No
            };
            var result = messageBoxDefaultResult;

            Application.Current.Dispatcher.Invoke(() =>
                result = MessageBox.Show(
                    promptText,
                    caption,
                    messageBoxButton,
                    messageBoxImage,
                    messageBoxDefaultResult));

            if (result == MessageBoxResult.OK || result == MessageBoxResult.Yes)
            {
                return true;
            }
            else if (result == MessageBoxResult.No
                || (result == MessageBoxResult.Cancel && messageBoxButton == MessageBoxButton.OKCancel))
            {
                return false;
            }
            else
            {
                return null;
            }
        }

        internal bool? ShowWindow(object viewModel)
        {
            Window window = null;
            bool? result = null;

            // ViewModels must be assigned and the window must
            // be shown explicitly from the main thread
            Application.Current.Dispatcher.Invoke(() =>
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

        private static IEnumerable<T> GetVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                    yield return (T)child;

                foreach (var descendant in GetVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}