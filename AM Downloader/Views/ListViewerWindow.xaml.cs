// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Windows;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for ListViewerWindow.xaml
    /// </summary>
    public partial class ListViewerWindow : Window
    {
        public ListViewerWindow()
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
    }
}