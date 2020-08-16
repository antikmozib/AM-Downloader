using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AMDownloader
{
    class DownloaderIconConverter : IValueConverter
    {
        private Dictionary<string, ImageSource> _icons = new Dictionary<string, ImageSource>();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value.ToString();
            string ext = Path.GetExtension(path);
            bool deleteTempFile = false;
            ImageSource imageSource;
            if (_icons.ContainsKey(ext))
            {
                _icons.TryGetValue(ext, out imageSource);
            }
            else
            {
                if (!File.Exists(path))
                {
                    File.Create(path).Close();
                    deleteTempFile = true;
                }
                var icon = Icon.ExtractAssociatedIcon(path);
                if (deleteTempFile)
                {
                    File.Delete(path);
                }
                imageSource = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                _icons.Add(ext, imageSource);
            }
            return imageSource;
            //return IconManager.FindIconForFilename(value.ToString(), false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
