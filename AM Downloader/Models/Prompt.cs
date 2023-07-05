// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System.Windows;
using System;

namespace AMDownloader.Models
{
    internal enum PromptButton
    {
        OK, OKCancel, YesNo, YesNoCancel
    }

    internal enum PromptIcon
    {
        None, Error, Question, Exclamation, Warning, Asterisk, Information
    }

    internal static class Prompt
    {
        public static bool? Show(
            string promptText,
            string caption,
            PromptButton button,
            PromptIcon icon,
            bool defaultResult = true,
            bool invokeAsync = false)
        {
            var messageBoxButton = button switch
            {
                PromptButton.OK => MessageBoxButton.OK,
                PromptButton.OKCancel => MessageBoxButton.OKCancel,
                PromptButton.YesNo => MessageBoxButton.YesNo,
                PromptButton.YesNoCancel => MessageBoxButton.YesNoCancel,
                _ => throw new ArgumentOutOfRangeException(nameof(button))
            };
            var messageBoxImage = icon switch
            {
                PromptIcon.None => MessageBoxImage.None,
                PromptIcon.Error => MessageBoxImage.Error,
                PromptIcon.Question => MessageBoxImage.Question,
                PromptIcon.Exclamation => MessageBoxImage.Exclamation,
                PromptIcon.Warning => MessageBoxImage.Warning,
                PromptIcon.Asterisk => MessageBoxImage.Asterisk,
                PromptIcon.Information => MessageBoxImage.Information,
                _ => throw new ArgumentOutOfRangeException(nameof(icon))
            };
            var messageBoxDefaultResult = defaultResult switch
            {
                true => messageBoxButton == MessageBoxButton.OK || messageBoxButton == MessageBoxButton.OKCancel
                    ? MessageBoxResult.OK
                    : MessageBoxResult.Yes,
                false => messageBoxButton == MessageBoxButton.OKCancel
                    ? MessageBoxResult.Cancel
                    : MessageBoxResult.No
            };
            var result = messageBoxDefaultResult;

            if (!invokeAsync)
            {
                Application.Current.Dispatcher.Invoke(() =>
                result = MessageBox.Show(
                    promptText,
                    caption,
                    messageBoxButton,
                    messageBoxImage,
                    messageBoxDefaultResult));
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                result = MessageBox.Show(
                    promptText,
                    caption,
                    messageBoxButton,
                    messageBoxImage,
                    messageBoxDefaultResult));
            }

            if (result == MessageBoxResult.OK || result == MessageBoxResult.Yes)
            {
                return true;
            }
            else if (result == MessageBoxResult.No
                || (result == MessageBoxResult.Cancel && messageBoxButton == MessageBoxButton.OKCancel))
            {
                return false;
            }
            else
            {
                return null;
            }
        }
    }
}
