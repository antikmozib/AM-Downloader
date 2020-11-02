// Copyright (C) 2020 Antik Mozib. Released under CC BY-NC-SA 4.0.

using System.Windows;
using System.Windows.Controls;

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