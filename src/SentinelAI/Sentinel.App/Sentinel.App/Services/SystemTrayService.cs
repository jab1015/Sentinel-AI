/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Windows.Input;

namespace Sentinel.App.Services
{
    public sealed class SystemTrayService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private bool _disposed;

        public SystemTrayService(Action showApplication, Action exitApplication)
        {
            ArgumentNullException.ThrowIfNull(showApplication);
            ArgumentNullException.ThrowIfNull(exitApplication);

            MenuFlyout contextMenu = new()
            {
                AreOpenCloseAnimationsEnabled = false
            };

            MenuFlyoutItem openItem = new()
            {
                Text = "Open Sentinel AI",
                Width = 180
            };
            MenuFlyoutItem exitItem = new()
            {
                Text = "Exit Sentinel AI"
            };

            openItem.Click += (_, _) => showApplication();
            exitItem.Click += (_, _) => exitApplication();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new MenuFlyoutSeparator());
            contextMenu.Items.Add(exitItem);

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Sentinel AI — monitoring your computer",
                IconSource = new GeneratedIconSource
                {
                    // Use the Unicode text-presentation shield so the tray branding remains
                    // deterministic and does not depend on a color emoji renderer or external file.
                    Text = "🛡︎",
                    FontSize = 38,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    Background = new SolidColorBrush(Colors.Crimson)
                },
                ContextFlyout = contextMenu,
                ContextMenuMode = ContextMenuMode.SecondWindow,
                LeftClickCommand = new RelayCommand(showApplication),
                Visibility = Visibility.Visible,
                NoLeftClickDelay = true
            };

            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
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
