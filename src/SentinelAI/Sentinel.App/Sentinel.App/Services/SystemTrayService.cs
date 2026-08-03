/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Windows.Input;

namespace Sentinel.App.Services
{
    public sealed class SystemTrayService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private bool _disposed;

        public SystemTrayService(Action showApplication, Action showOptions, Action exitApplication)
        {
            ArgumentNullException.ThrowIfNull(showApplication);
            ArgumentNullException.ThrowIfNull(showOptions);
            ArgumentNullException.ThrowIfNull(exitApplication);

            MenuFlyout contextMenu = new()
            {
                AreOpenCloseAnimationsEnabled = false
            };

            MenuFlyoutItem openItem = new()
            {
                Text = "Open Sentinel AI",
                Width = 180,
                Command = new RelayCommand(showApplication)
            };
            MenuFlyoutItem optionsItem = new()
            {
                Text = "Options",
                Command = new RelayCommand(showOptions)
            };
            MenuFlyoutItem exitItem = new()
            {
                Text = "Exit Sentinel AI",
                Command = new RelayCommand(exitApplication)
            };

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(optionsItem);
            contextMenu.Items.Add(new MenuFlyoutSeparator());
            contextMenu.Items.Add(exitItem);

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Sentinel AI — monitoring your computer",
                IconSource = CreateTrayIconSource(),
                ContextFlyout = contextMenu,
                ContextMenuMode = ContextMenuMode.SecondWindow,
                LeftClickCommand = new RelayCommand(showApplication),
                Visibility = Visibility.Visible,
                NoLeftClickDelay = true
            };

            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        }

        private static ImageSource CreateTrayIconSource()
        {
            try
            {
                return new BitmapImage(new Uri("ms-appx:///Assets/Shield.ico"));
            }
            catch
            {
                return new GeneratedIconSource
                {
                    Text = "S",
                    FontSize = 34,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    Background = new SolidColorBrush(Colors.Crimson)
                };
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _trayIcon.Dispose();
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            }

            public event EventHandler? CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) => _execute();
        }
    }
}
