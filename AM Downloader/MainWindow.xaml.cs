// Copyright (C) 2020 Antik Mozib.

using AMDownloader.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ICollectionView _dataView = null;
        private readonly DownloaderViewModel _primaryViewModel;
        private static readonly string _appGuid = "20d3be33-cd45-4c69-b038-e95bc434e09c";
        private static readonly Mutex _mutex = new Mutex(false, "Global\\" + _appGuid);
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection? _lastDirection = null;

        public MainWindow()
        {
            _primaryViewModel = new DownloaderViewModel(DisplayMessage, ShowUrlList);

            InitializeComponent();
            if (!_mutex.WaitOne(0, false))
            {
                var name = Assembly.GetExecutingAssembly().GetName().Name;
                MessageBox.Show(
                    "Another instance of " + name + " is already running.",
                    name, MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }

            DataContext = _primaryViewModel;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Settings.Default.SelectedColumnHeader != null)
            {
                GridViewColumn gridViewColumn =
                    ((GridView)lvDownload.View).Columns.FirstOrDefault(
                        x => (string)x.Header == Settings.Default.SelectedColumnHeader);
                if (gridViewColumn != null)
                {
                    List<GridViewColumnHeader> headers = GetVisualChildren<GridViewColumnHeader>(lvDownload).ToList();
                    GridViewColumnHeader gridViewColumnHeader = null;

                    foreach (GridViewColumnHeader header in headers)
                    {
                        if (header.Column == null || header.Column.Header == null) continue;

                        if ((header.Column.Header as string).Equals(gridViewColumn.Header as string))
                        {
                            gridViewColumnHeader = header;
                            break;
                        }
                    }

                    if (gridViewColumnHeader != null)
                    {
                        Sort(gridViewColumnHeader, Settings.Default.SelectedColumnHeaderDirection);
                    }

                }
            }
        }

        private void menuExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void menuAbout_Click(object sender, RoutedEventArgs e)
        {
            var name = Assembly.GetExecutingAssembly().GetName().Name;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var copyright = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(
                AssemblyCopyrightAttribute), true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright;
            var website = "https://mozib.io/amdownloader";

            MessageBox.Show(
                name + "\nVersion " + version + "\n\n" + copyright + "\n" + website + "\n\n" +
                "DISCLAIMER: This is free software. There is NO warranty; " +
                "not even for MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void tvCategories_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Settings.Default.LastSelectedCatagory))
            {
                (tvCategories.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem).IsSelected = true;
            }
            else
            {
                (tvCategories.ItemContainerGenerator.ContainerFromIndex(
                    (int)(Categories)Enum.Parse(typeof(Categories), Settings.Default.LastSelectedCatagory)) as TreeViewItem).IsSelected = true;
            }
        }

        private void lvDownload_HeaderClick(object sender, RoutedEventArgs e)
        {
            ListSortDirection? direction = ListSortDirection.Ascending;

            var headerClicked = e.OriginalSource as GridViewColumnHeader;

            if (headerClicked == null || headerClicked.Role == GridViewColumnHeaderRole.Padding)
            {
                return;
            }

            if (headerClicked == _lastHeaderClicked)
            {
                if (_lastDirection == ListSortDirection.Ascending)
                {
                    direction = ListSortDirection.Descending;
                }
                else if (_lastDirection == ListSortDirection.Descending)
                {
                    direction = null;
                }
                else
                {
                    direction = ListSortDirection.Ascending;
                }
            }

            Sort(headerClicked, direction);
        }

        private void Sort(GridViewColumnHeader columnHeader, ListSortDirection? direction)
        {

            if (columnHeader == null)
            {
                return;
            }

            if (_dataView == null)
            {
                _dataView = CollectionViewSource.GetDefaultView(lvDownload.ItemsSource);
            }

            var columnBinding = columnHeader.Column.DisplayMemberBinding as Binding;
            var sortBy = columnBinding?.Path.Path ?? columnHeader.Column.Header as string;

            switch (sortBy.ToLower())
            {
                case null:
                    Settings.Default.SelectedColumnHeader = null;
                    Settings.Default.Save();
                    return;

                case "downloaded":
                    sortBy = "TotalBytesCompleted";
                    break;

                case "size":
                    sortBy = "TotalBytesToDownload";
                    break;

                case "location":
                    sortBy = "Destination";
                    break;

                case "url":
                    sortBy = "Url";
                    break;

                case "created":
                    sortBy = "DateCreated";
                    break;

                case "http status":
                    sortBy = "StatusCode";
                    break;

                case "eta":
                    sortBy = "TimeRemaining";
                    break;

                case "type":
                    sortBy = "Extension";
                    break;

                case "connections":
                    sortBy = "NumberOfActiveStreams";
                    break;
            }

            _dataView.SortDescriptions.Clear();

            if (direction != null)
            {
                SortDescription sd = new SortDescription(sortBy, direction ?? ListSortDirection.Ascending);
                _dataView.SortDescriptions.Add(sd);
            }

            if (_lastHeaderClicked != null)
            {
                _lastHeaderClicked.Column.HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate;
            }

            if (direction == ListSortDirection.Ascending)
            {
                columnHeader.Column.HeaderTemplate =
                  Resources["HeaderTemplateArrowDown"] as DataTemplate;
            }
            else if (direction == ListSortDirection.Descending)
            {
                columnHeader.Column.HeaderTemplate =
                  Resources["HeaderTemplateArrowUp"] as DataTemplate;
            }
            else
            {
                columnHeader.Column.HeaderTemplate =
                  Resources["HeaderTemplate"] as DataTemplate;
            }

            _dataView.Refresh();
            _lastHeaderClicked = columnHeader;
            _lastDirection = direction;


            Settings.Default.SelectedColumnHeader = columnHeader.Column.Header.ToString();
            Settings.Default.SelectedColumnHeaderDirection = direction ?? ListSortDirection.Ascending;
            Settings.Default.Save();
        }

        internal MessageBoxResult DisplayMessage(
            string message,
            string title = "",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.Information,
            MessageBoxResult defaultResult = MessageBoxResult.OK)
        {
            if (title == "")
            {
                title = Assembly.GetExecutingAssembly().GetName().Name;
            }
            return Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                this,
                message,
                title,
                button,
                image,
                defaultResult));
        }

        internal void ShowUrlList(List<string> urls, string caption, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var previewViewModel = new PreviewViewModel(message, urls);
                var previewWindow = new PreviewWindow();
                previewWindow.DataContext = previewViewModel;
                previewWindow.Owner = this;
                previewWindow.Title = caption;
                previewWindow.ShowDialog();
            });
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