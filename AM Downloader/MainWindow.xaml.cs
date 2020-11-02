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
                MessageBox.Show("Another instance of " + name + " is already running.", name, MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }

            DataContext = _primaryViewModel;
        }

        private void menuExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void menuAbout_Click(object sender, RoutedEventArgs e)
        {
            var name = Assembly.GetExecutingAssembly().GetName().Name;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var description = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false).OfType<AssemblyDescriptionAttribute>().FirstOrDefault()?.Description;
            var copyright = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright;
            var funFact = "Total data transferred over lifetime: " + Math.Round((double)Settings.Default.BytesTransferredOverLifetime / (1024 * 1024 * 1024), 2) + " GB";
            MessageBox.Show(name + "\nVersion " + version + "\n\n" + description + "\n\n" + copyright + "\n\n" + funFact, "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void lvDownload_HeaderClick(object sender, RoutedEventArgs e)
        {
            ListSortDirection? direction = ListSortDirection.Ascending;
            var headerClicked = e.OriginalSource as GridViewColumnHeader;
            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
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

                    if (_lastHeaderClicked != null)
                    {
                        _lastHeaderClicked.Column.HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate;
                    }

                    if (direction == ListSortDirection.Ascending)
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplateArrowDown"] as DataTemplate;
                    }
                    else if (direction == ListSortDirection.Descending)
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    }
                    else
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplate"] as DataTemplate;
                    }

                    _dataView.Refresh();
                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }

        private void Sort(GridViewColumnHeader columnHeader, ListSortDirection? direction)
        {
            if (_dataView == null)
            {
                _dataView = CollectionViewSource.GetDefaultView(lvDownload.ItemsSource);
            }
            _dataView.SortDescriptions.Clear();

            if (direction == null) return;
            var columnBinding = columnHeader.Column.DisplayMemberBinding as Binding;
            var sortBy = columnBinding?.Path.Path ?? columnHeader.Column.Header as string;
            switch (sortBy.ToLower())
            {
                case null:
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
            SortDescription sd = new SortDescription(sortBy, direction ?? ListSortDirection.Ascending);
            _dataView.SortDescriptions.Add(sd);
        }

        private void tvCategories_Loaded(object sender, RoutedEventArgs e)
        {
            (tvCategories.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem).IsSelected = true;
        }

        internal MessageBoxResult DisplayMessage(string message, string title = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information, MessageBoxResult defaultResult = MessageBoxResult.OK)
        {
            if (title == "")
            {
                title = Assembly.GetExecutingAssembly().GetName().Name;
            }
            return Application.Current.Dispatcher.Invoke(() => MessageBox.Show(this, message, title, button, image, defaultResult));
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
    }
}