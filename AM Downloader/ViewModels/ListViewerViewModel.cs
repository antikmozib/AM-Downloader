// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace AMDownloader.ViewModels
{
    public class ListViewerViewModel : INotifyPropertyChanged
    {
        public struct ListViewerItem
        {
            public string Content { get; set; }

            public override readonly string ToString()
            {
                return Content;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand CopyCommand { get; private set; }

        public string Title { get; }
        public string Description { get; }
        public ObservableCollection<ListViewerItem> ListViewerItems { get; }

        public ListViewerViewModel(List<string> items, string description, string title)
        {
            CopyCommand = new RelayCommand<object>(Copy, Copy_CanExecute);

            Title = title;
            Description = description;
            ListViewerItems = new ObservableCollection<ListViewerItem>();

            foreach (var item in items)
            {
                var listViewerItem = new ListViewerItem
                {
                    Content = item
                };

                ListViewerItems.Add(listViewerItem);
            }
        }

        private void Copy(object obj)
        {
            var items = (obj as ObservableCollection<object>).Cast<ListViewerItem>();
            var output = string.Join(Environment.NewLine, items.Select(o => o.Content));

            ClipboardObserver.Clear();
            ClipboardObserver.SetText(output);
        }

        private bool Copy_CanExecute(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            return (obj as ObservableCollection<object>).Cast<ListViewerItem>().Any();
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}