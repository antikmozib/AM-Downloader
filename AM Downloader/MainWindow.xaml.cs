// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Common;
using AMDownloader.ObjectModel;
using AMDownloader.ObjectModel.Serializable;
using AMDownloader.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using System.Xml.Serialization;
using WinForms = System.Windows.Forms;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string _appGuid = "20d3be33-cd45-4c69-b038-e95bc434e09c";
        private static readonly Mutex _mutex = new(false, "Global\\" + _appGuid);
        private readonly DownloaderViewModel _primaryViewModel;
        private ICollectionView _dataView = null;
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection? _lastDirection = null;

        public MainWindow()
        {
            _primaryViewModel = new DownloaderViewModel(ShowPrompt, ShowWindow);

            InitializeComponent();

            DataContext = _primaryViewModel;

            if (!_mutex.WaitOne(0, false))
            {
                var name = Assembly.GetExecutingAssembly().GetName().Name;
                MessageBox.Show(
                    "Another instance of " + name + " is already running.",
                    name, MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
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

                Settings.Default.FirstRun = false;
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
                    SerializableUIColumnList restoreCols;
                    var xmlReader = new XmlSerializer(typeof(SerializableUIColumnList));

                    using (var streamReader = new StreamReader(Paths.UIColumnOrderFile))
                    {
                        restoreCols = (SerializableUIColumnList)xmlReader.Deserialize(streamReader);
                    }

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
                when (ex is XmlException || ex is InvalidOperationException)
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
                    var writer = new XmlSerializer(typeof(SerializableUIColumnList));

                    Directory.CreateDirectory(Paths.LocalAppDataFolder);

                    using var streamWriter = new StreamWriter(Paths.UIColumnOrderFile, false);
                    writer.Serialize(streamWriter, columnOrderList);
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
                $"{name}\nVersion {version}\n\n{copyright}\n\n{website}\n\n"
                + "DISCLAIMER: This is free software. There is NO warranty; "
                + "not even for MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.\n\n"
                + $"Total downloaded since installation: {totalDownloaded.ToString("n0", cultureInfo)} MB",
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

            // link the column header string value to the prop of the binded obj
            switch (columnToSort.ToLower())
            {
                case "downloaded":
                    columnToSort = "BytesDownloaded";
                    break;

                case "size":
                    columnToSort = "TotalBytesToDownload";
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

                case "eta":
                    columnToSort = "TimeRemaining";
                    break;

                case "type":
                    columnToSort = "Extension";
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
                columnHeader.Column.HeaderTemplate = Resources["HeaderTemplateArrowUp"] as DataTemplate;
            }
            else if (direction == ListSortDirection.Ascending)
            {
                columnHeader.Column.HeaderTemplate = Resources["HeaderTemplateArrowDown"] as DataTemplate;
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

        private bool? ShowPrompt(
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

        private bool? ShowWindow(object viewModel)
        {
            Window window = null;
            bool? result = null;

            // ViewModels must be assigned and the window must
            // be shown explicitly from the main thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (viewModel is AddDownloadViewModel)
                {
                    window = new AddDownloadWindow();
                }
                else if (viewModel is OptionsViewModel)
                {
                    window = new OptionsWindow();
                }
                else if (viewModel is ListViewerViewModel)
                {
                    window = new ListViewerWindow();
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