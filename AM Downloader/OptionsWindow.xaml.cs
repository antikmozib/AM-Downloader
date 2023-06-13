// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Windows;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class OptionsWindow : Window
    {
        public OptionsWindow()
        {
            InitializeComponent();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Please restart the app for the changes to take effect.", 
                "Reset", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);

            this.Close();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}