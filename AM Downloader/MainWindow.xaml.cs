using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AMDownloader.Properties;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace AMDownloader
{
    delegate void CloseApplicationDelegate();

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private ICollectionView _dataView = null;
        private readonly DownloaderViewModel _primaryViewModel = new DownloaderViewModel(new CloseApplicationDelegate(CloseApplication));
        private static readonly string _appGuid = "20d3be33-cd45-4c69-b038-e95bc434e09c";
        private static readonly Mutex _mutex = new Mutex(false, "Global\\" + _appGuid);
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection? _lastDirection = null;

        public MainWindow()
        {
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

        private async void menuAbout_Click(object sender, RoutedEventArgs e)
        {
            var name = Assembly.GetExecutingAssembly().GetName().Name;
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var description = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false).OfType<AssemblyDescriptionAttribute>().FirstOrDefault()?.Description;
            var copyright = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright;
            var funFact = "Total data transferred over lifetime: " + Math.Round((double)Settings.Default.BytesTransferredOverLifetime / (1024 * 1024 * 1024), 2) + " GB";
            await this.ShowMessageAsync("About", name + "\nVersion " + version + "\n\n" + description + "\n\n" + copyright + "\n\n" + funFact);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            this.Title = "Quitting, please wait...";
            this.IsEnabled = false;
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
            if (direction == null) return;
            if (_dataView == null)
            {
                _dataView = CollectionViewSource.GetDefaultView(lvDownload.ItemsSource);
            }
            _dataView.SortDescriptions.Clear();
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

        internal static void CloseApplication()
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(Application.Current.Shutdown);
            }
            catch
            {

            }
        }
    }
}
