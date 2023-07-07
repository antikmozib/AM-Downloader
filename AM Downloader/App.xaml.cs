// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using AMDownloader.Helpers;
using AMDownloader.Properties;
using AMDownloader.Views;
using Serilog;

namespace AMDownloader
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly string _appGuid = "20d3be33-cd45-4c69-b038-e95bc434e09c";
        private static readonly Mutex _mutex = new(false, "Global\\" + _appGuid);

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (!_mutex.WaitOne(0, false))
            {
                var name = Assembly.GetExecutingAssembly().GetName().Name;
                MessageBox.Show(
                    "Another instance of " + name + " is already running.",
                    name, MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
            }
            else
            {
                Log.Debug("Starting app...");

                ++Settings.Default.LaunchCount;

                string[] args = Environment.GetCommandLineArgs();
                var debugLoggerConfig = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Debug()
                    .WriteTo.File(Common.Paths.LogFile, rollingInterval: RollingInterval.Day)
                    .CreateLogger();
                var releaseLoggerConfig = new LoggerConfiguration()
                    .MinimumLevel.Error()
                    .WriteTo.File(Common.Paths.LogFile, rollingInterval: RollingInterval.Day)
                    .CreateLogger();

#if DEBUG
                Log.Logger = debugLoggerConfig;
#else
                if (args.Select(o => o.ToLower()).Contains("-debug"))
                {
                    Log.Logger = debugLoggerConfig;
                    Log.Debug("Debug logging configuration enabled.");
                }
                else
                {
                    Log.Logger = releaseLoggerConfig;
                }
#endif

                MainView mainView = new();
                mainView.Show();
            }
        }
    }
}