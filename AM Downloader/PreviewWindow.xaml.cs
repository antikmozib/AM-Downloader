// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Windows;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for PreviewWindow.xaml
    /// </summary>
    public partial class PreviewWindow : Window
    {
        public PreviewWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SelectAll(object sender, RoutedEventArgs e)
        {
            listUrls.SelectAll();
        }
    }
}