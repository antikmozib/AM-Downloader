using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class OptionsWindow : Window
    {
        private OptionsViewModel optionsViewModel = new OptionsViewModel();

        public OptionsWindow()
        {
            InitializeComponent();
            DataContext = optionsViewModel;
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {            
            this.Close();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
