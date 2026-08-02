/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sentinel.App.Services
{
    public sealed class SystemTrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private bool _disposed;

        public SystemTrayService(Action showApplication, Action exitApplication)
        {
            ArgumentNullException.ThrowIfNull(showApplication);
            ArgumentNullException.ThrowIfNull(exitApplication);

            _menu = new ContextMenuStrip();
            ToolStripMenuItem openItem = new("Open Sentinel AI");
            ToolStripMenuItem exitItem = new("Exit Sentinel AI");

            openItem.Click += (_, _) => showApplication();
            exitItem.Click += (_, _) => exitApplication();

            _menu.Items.Add(openItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            Icon icon = GetApplicationIcon();
            _notifyIcon = new NotifyIcon
            {
                Text = "Sentinel AI — monitoring your computer",
                Icon = icon,
                ContextMenuStrip = _menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (_, _) => showApplication();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        private static Icon GetApplicationIcon()
        {
            string? executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                Icon? extractedIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (extractedIcon is not null)
                {
                    return extractedIcon;
                }
            }

            return SystemIcons.Shield;
        }
    }
}
