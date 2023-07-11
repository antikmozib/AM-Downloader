// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.Helpers;
using System.Windows;

namespace AMDownloader.Views
{
    /// <summary>
    /// Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class SettingsView : Window
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            string msg = (string)Application.Current.FindResource("resetSettingsMsg");
            MessageBox.Show(msg, "Reset", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Window_SourceInitialized(object sender, System.EventArgs e)
        {
            WindowUtils.RemoveIcon(this);
        }
    }
}