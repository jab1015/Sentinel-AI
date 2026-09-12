using System;
using System.IO;
using System.Text.Json;

namespace Sentinel.App.Services
{
    internal sealed class OptimizationRuntimeStateStore
    {
        private readonly string _path;
        internal OptimizationRuntimeStateStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

        internal bool TryLoad(out OptimizationRuntimeState state)
        {
            state = OptimizationRuntimeState.Empty;
            try
            {
                if (!File.Exists(_path)) return true;
                OptimizationRuntimeState? loaded = JsonSerializer.Deserialize<OptimizationRuntimeState>(File.ReadAllText(_path));
                if (loaded is null) return false;
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
                string directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("State path has no directory.");
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".optimization-runtime-state.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, _path, overwrite: true);
                OptimizationRuntimeState? verified = JsonSerializer.Deserialize<OptimizationRuntimeState>(File.ReadAllText(_path));
                return verified is not null && verified.LastAttemptUtc == state.LastAttemptUtc;
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
    }

    internal sealed record OptimizationRuntimeState(DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastSucceededUtc, string LastSummary)
    {
        internal static OptimizationRuntimeState Empty { get; } = new(null, null, string.Empty);
    }
}
