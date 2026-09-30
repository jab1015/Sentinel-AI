/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Text;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Minimal synchronous startup breadcrumb log that is safe to call before
    /// WinUI, package identity, Store, monitoring, or async logging is ready.
    /// </summary>
    public static class BootstrapLaunchLog
    {
        private static readonly object Gate = new();

        public static string LogPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, "Modern Methods", "Sentinel AI", "Logs", "bootstrap-launch.log");
            }
        }

        public static void Write(string stage, Exception? exception = null)
        {
            try
            {
                lock (Gate)
                {
                    string path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    string detail = exception is null
                        ? string.Empty
                        : $" | {exception.GetType().Name}: {Sanitize(exception.Message)}";
                    string line = $"{DateTimeOffset.UtcNow:O} | PID={Environment.ProcessId} | {Sanitize(stage)}{detail}{Environment.NewLine}";
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Startup diagnostics must never replace the original startup path.
            }
        }

        private static string Sanitize(string value) =>
            (value ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
    }
}
