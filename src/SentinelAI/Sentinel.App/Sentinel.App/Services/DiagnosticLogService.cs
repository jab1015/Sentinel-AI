/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class DiagnosticLogService
    {
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private const int MaxLineChars = 16_384;
        private static readonly SemaphoreSlim GlobalWriteGate = new(1, 1);
        private static readonly object CrashWriteGate = new();
        private readonly string _logDirectory;
        private readonly string _logPath;
        private readonly string _previousLogPath;
        private readonly string _crashBreadcrumbPath;

        public DiagnosticLogService()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(localAppData, "Modern Methods", "Sentinel AI", "Logs");
            _logPath = Path.Combine(_logDirectory, "sentinel.log");
            _previousLogPath = Path.Combine(_logDirectory, "sentinel.previous.log");
            _crashBreadcrumbPath = Path.Combine(_logDirectory, "last-crash.txt");
        }

        public string LogPath => _logPath;

        public Task InformationAsync(string eventName, string message) => WriteAsync("INFO", eventName, message, null);
        public Task WarningAsync(string eventName, string message) => WriteAsync("WARN", eventName, message, null);
        public Task ErrorAsync(string eventName, string message, Exception? exception = null) => WriteAsync("ERROR", eventName, message, exception);

        /// <summary>
        /// Best-effort bounded synchronous breadcrumb for a process-terminating path.
        /// It does not wait on the normal async gate and never throws.
        /// </summary>
        public void WriteCrashBreadcrumb(string eventName, Exception? exception)
        {
            try
            {
                lock (CrashWriteGate)
                {
                    Directory.CreateDirectory(_logDirectory);
                    string text = BuildLine("CRASH", eventName, "Unhandled application exception reached the process boundary.", exception);
                    using FileStream stream = new(_crashBreadcrumbPath, FileMode.Create, FileAccess.Write, FileShare.Read,
                        4096, FileOptions.WriteThrough);
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
            }
            catch
            {
                // A crash breadcrumb must never replace the original failure.
            }
        }

        private async Task WriteAsync(string level, string eventName, string message, Exception? exception)
        {
            await GlobalWriteGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_logDirectory);
                RotateIfNeeded();
                string line = BuildLine(level, eventName, message, exception);
                await File.AppendAllTextAsync(_logPath, line, Encoding.UTF8).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                GlobalWriteGate.Release();
            }
        }

        private void RotateIfNeeded()
        {
            if (!File.Exists(_logPath) || new FileInfo(_logPath).Length < MaxLogBytes) return;
            string tempPrevious = _previousLogPath + ".tmp";
            if (File.Exists(tempPrevious)) File.Delete(tempPrevious);
            File.Copy(_logPath, tempPrevious, overwrite: true);
            File.Move(tempPrevious, _previousLogPath, overwrite: true);
            using FileStream truncate = new(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            truncate.Flush(flushToDisk: true);
        }

        private static string BuildLine(string level, string eventName, string message, Exception? exception)
        {
            string safeEvent = Scrub(Normalize(string.IsNullOrWhiteSpace(eventName) ? "General" : eventName));
            string safeMessage = Scrub(Normalize(message ?? string.Empty));
            string exceptionText = exception is null ? string.Empty : " | " + FormatException(exception);
            string line = $"{DateTimeOffset.UtcNow:O} | {level} | {safeEvent} | {safeMessage}{exceptionText}";
            if (line.Length > MaxLineChars) line = line[..MaxLineChars] + "…";
            return line + Environment.NewLine;
        }

        private static string FormatException(Exception exception)
        {
            StringBuilder builder = new();
            int depth = 0;
            for (Exception? current = exception; current is not null && depth < 4; current = current.InnerException, depth++)
            {
                if (depth > 0) builder.Append(" | inner: ");
                builder.Append(current.GetType().Name).Append(": ").Append(Scrub(Normalize(current.Message)));
                if (!string.IsNullOrWhiteSpace(current.StackTrace))
                    builder.Append(" | stack: ").Append(Scrub(Normalize(current.StackTrace)));
            }
            return builder.ToString();
        }

        private static string Scrub(string value)
        {
            string result = value;
            result = Regex.Replace(result, @"(?i)\b(authorization|api[_ -]?key|token|password|passwd|secret)\s*[:=]\s*[^\s,;]+", "$1=[redacted]");
            result = Regex.Replace(result, @"(?i)\bBearer\s+[A-Za-z0-9._~+\-/]+=*", "Bearer [redacted]");
            result = Regex.Replace(result, @"(?i)\b[A-Z]:\\Users\\[^\\\s]+", @"C:\Users\[redacted-user]");
            return result;
        }

        private static string Normalize(string value) =>
            value.Replace("\r", " ", StringComparison.Ordinal)
                 .Replace("\n", " ", StringComparison.Ordinal)
                 .Trim();
    }
}
