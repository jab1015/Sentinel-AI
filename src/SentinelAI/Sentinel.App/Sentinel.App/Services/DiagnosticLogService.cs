/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides a small, durable application diagnostic log for production support.
    /// Logging failures are deliberately non-fatal and never interrupt protection.
    /// </summary>
    public sealed class DiagnosticLogService
    {
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly string _logDirectory;
        private readonly string _logPath;
        private readonly string _previousLogPath;

        public DiagnosticLogService()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(localAppData, "Modern Methods", "Sentinel AI", "Logs");
            _logPath = Path.Combine(_logDirectory, "sentinel.log");
            _previousLogPath = Path.Combine(_logDirectory, "sentinel.previous.log");
        }

        public string LogPath => _logPath;

        public Task InformationAsync(string eventName, string message) =>
            WriteAsync("INFO", eventName, message, null);

        public Task WarningAsync(string eventName, string message) =>
            WriteAsync("WARN", eventName, message, null);

        public Task ErrorAsync(string eventName, string message, Exception? exception = null) =>
            WriteAsync("ERROR", eventName, message, exception);

        private async Task WriteAsync(string level, string eventName, string message, Exception? exception)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                eventName = "General";
            }

            message ??= string.Empty;

            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_logDirectory);
                RotateIfNeeded();

                string safeMessage = Normalize(message);
                string exceptionText = exception is null
                    ? string.Empty
                    : $" | {exception.GetType().Name}: {Normalize(exception.Message)}";
                string line = $"{DateTimeOffset.Now:O} | {level} | {Normalize(eventName)} | {safeMessage}{exceptionText}{Environment.NewLine}";

                await File.AppendAllTextAsync(_logPath, line, Encoding.UTF8).ConfigureAwait(false);
            }
            catch
            {
                // Diagnostics must never become an application failure path.
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private void RotateIfNeeded()
        {
            if (!File.Exists(_logPath) || new FileInfo(_logPath).Length < MaxLogBytes)
            {
                return;
            }

            if (File.Exists(_previousLogPath))
            {
                File.Delete(_previousLogPath);
            }

            File.Move(_logPath, _previousLogPath);
        }

        private static string Normalize(string value) =>
            value.Replace("\r", " ", StringComparison.Ordinal)
                 .Replace("\n", " ", StringComparison.Ordinal)
                 .Trim();
    }
}
