// Copyright (C) 2020-2025 Antik Mozib. All rights reserved.

using System.Windows;
using System;

namespace AMDownloader.Models
{
    public enum PromptButton
    {
        OK, OKCancel, YesNo, YesNoCancel
    }

    public enum PromptIcon
    {
        None, Error, Question, Exclamation, Warning, Asterisk, Information
    }

    public static class Prompt
    {
        public static bool? Show(
            string promptText,
            string caption,
            PromptButton button = PromptButton.OK,
            PromptIcon icon = PromptIcon.None,
            bool defaultResult = true)
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
            var result = MessageBox.Show(
                   promptText,
                   caption,
                   messageBoxButton,
                   messageBoxImage,
                   messageBoxDefaultResult);

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
