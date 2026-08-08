/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists user optimization preferences outside package-specific storage so
    /// settings work consistently in installed and development runs.
    /// </summary>
    public sealed class OptimizationSettingsService
    {
        private static readonly object SettingsGate = new();
        private readonly string _settingsPath;

        public OptimizationSettingsService()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI");

            Directory.CreateDirectory(directory);
            _settingsPath = Path.Combine(directory, "optimization-settings.json");
        }

        public OptimizationSettings Load()
        {
            lock (SettingsGate)
            {
                try
                {
                    if (!File.Exists(_settingsPath))
                        return OptimizationSettings.Default;

                    string json = File.ReadAllText(_settingsPath);
                    OptimizationSettings? settings = JsonSerializer.Deserialize<OptimizationSettings>(json);
                    return settings ?? OptimizationSettings.Default;
                }
                catch
                {
                    return OptimizationSettings.Default;
                }
            }
        }

        public bool Save(OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            lock (SettingsGate)
            {
                string? temporaryPath = null;
                try
                {
                    string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    string directory = Path.GetDirectoryName(_settingsPath)!;
                    temporaryPath = Path.Combine(
                        directory,
                        $".optimization-settings.{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(temporaryPath, json);
                    File.Move(temporaryPath, _settingsPath, overwrite: true);
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(temporaryPath))
                    {
                        try { File.Delete(temporaryPath); }
                        catch { }
                    }
                }
            }
        }
    }

    public sealed record OptimizationSettings(
        bool AutomaticOptimizationEnabled,
        OptimizationMode Mode,
        bool VerifyEveryChange,
        bool RollBackWhenPossible)
    {
        // Changing Windows automatically is an explicit user choice. Fresh installs
        // and unreadable/corrupt settings therefore fail closed until the user opts in.
        public static OptimizationSettings Default { get; } = new(
            AutomaticOptimizationEnabled: false,
            Mode: OptimizationMode.Conservative,
            VerifyEveryChange: true,
            RollBackWhenPossible: true);
    }

    public enum OptimizationMode
    {
        Conservative,
        Balanced,
        Advanced
    }
}
