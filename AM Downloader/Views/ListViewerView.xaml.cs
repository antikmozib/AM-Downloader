// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using System.Windows;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for ListViewerWindow.xaml
    /// </summary>
    public partial class ListViewerView : Window
    {
        public ListViewerView()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SelectAllMenuItem(object sender, RoutedEventArgs e)
        {
            ItemsListBox.SelectAll();
        }

        private void Window_SourceInitialized(object sender, System.EventArgs e)
        {
            WindowUtils.RemoveIcon(this);
            WindowUtils.RemoveMaxMinBox(this);
        }
    }
}