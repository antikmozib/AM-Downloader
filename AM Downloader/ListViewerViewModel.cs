﻿// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.ClipboardObservation;
using AMDownloader.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace AMDownloader
{
    public class ListViewerViewModel : INotifyPropertyChanged
    {
        public struct UrlListType
        {
            public string Url { get; set; }

            public override readonly string ToString()
            {
                return Url;
            }
        }

        private readonly ClipboardObserver _clipboard;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand CopyCommand { get; private set; }
        public string Description { get; set; }
        public ObservableCollection<UrlListType> Urls { get; set; }

        public ListViewerViewModel(string description, List<string> urls)
        {
            CopyCommand = new RelayCommand<object>(Copy);
            _clipboard = new ClipboardObserver();
            this.Description = description;
            this.Urls = new ObservableCollection<UrlListType>();
            foreach (var url in urls)
            {
                var urlListType = new UrlListType();
                urlListType.Url = url;
                Urls.Add(urlListType);
            }
        }

        private void Copy(object obj)
        {
            var urls = (obj as ObservableCollection<object>).Cast<UrlListType>();
            string output = "";
            foreach (var item in urls)
            {
                output += item.Url + '\n';
            }
            _clipboard.Clear();
            _clipboard.SetText(output);
        }

        protected void RaisePropertyChanged(string prop)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}