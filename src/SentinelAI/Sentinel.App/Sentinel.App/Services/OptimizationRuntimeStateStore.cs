using System;
using System.IO;
using System.Text.Json;

namespace Sentinel.App.Services
{
    internal sealed class OptimizationRuntimeStateStore
    {
        private const int MaximumStateBytes = 64 * 1024;
        private const int MaximumSummaryCharacters = 4096;
        private readonly string _path;
        private readonly string _leasePath;

        internal OptimizationRuntimeStateStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _leasePath = _path + ".lock";
        }

        internal bool TryAcquireExecutionLease(out IDisposable? lease)
        {
            lease = null;
            try
            {
                string directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("State path has no directory.");
                Directory.CreateDirectory(directory);
                lease = new FileStream(_leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch
            {
                lease?.Dispose();
                lease = null;
                return false;
            }
        }

        internal bool TryLoad(out OptimizationRuntimeState state)
        {
            state = OptimizationRuntimeState.Empty;
            try
            {
                if (!File.Exists(_path)) return true;
                if (!TryReadState(_path, out OptimizationRuntimeState? loaded) || loaded is null)
                    return false;
                state = loaded;
                return true;
            }
            catch { return false; }
        }

        internal bool TrySave(OptimizationRuntimeState state)
        {
            string? temporaryPath = null;
            try
            {
                if (!IsValidPersistedState(state)) return false;

                string directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("State path has no directory.");
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".optimization-runtime-state.{Guid.NewGuid():N}.tmp");

                byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true });
                if (serialized.Length <= 0 || serialized.Length > MaximumStateBytes) return false;

                using (FileStream temporary = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    temporary.Write(serialized, 0, serialized.Length);
                    temporary.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _path, overwrite: true);
                temporaryPath = null;

                return TryReadState(_path, out OptimizationRuntimeState? verified) &&
                    verified is not null &&
                    verified == state;
            }
            catch { return false; }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }

        private static bool TryReadState(string path, out OptimizationRuntimeState? state)
        {
            state = null;
            try
            {
                using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length <= 0 || input.Length > MaximumStateBytes) return false;

                byte[] buffer = new byte[MaximumStateBytes + 1];
                int total = 0;
                while (total < buffer.Length)
                {
                    int read = input.Read(buffer, total, buffer.Length - total);
                    if (read == 0) break;
                    total += read;
                }

                if (total <= 0 || total > MaximumStateBytes) return false;
                if (input.ReadByte() != -1) return false;

                OptimizationRuntimeState? loaded = JsonSerializer.Deserialize<OptimizationRuntimeState>(buffer.AsSpan(0, total));
                if (loaded is null || !IsValidPersistedState(loaded)) return false;
                state = loaded;
                return true;
            }
            catch { return false; }
        }

        private static bool IsValidPersistedState(OptimizationRuntimeState state)
        {
            if (state.LastAttemptUtc is null) return false;
            if (state.LastSummary is null || state.LastSummary.Length > MaximumSummaryCharacters) return false;
            if (state.LastSucceededUtc.HasValue && state.LastSucceededUtc.Value > state.LastAttemptUtc.Value) return false;
            return true;
        }
    }

    internal sealed record OptimizationRuntimeState(DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastSucceededUtc, string LastSummary)
    {
        internal static OptimizationRuntimeState Empty { get; } = new(null, null, string.Empty);
    }
}
