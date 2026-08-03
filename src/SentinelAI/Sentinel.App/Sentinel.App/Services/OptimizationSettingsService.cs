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

        public bool Save(OptimizationSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed record OptimizationSettings(
        bool AutomaticOptimizationEnabled,
        OptimizationMode Mode,
        bool VerifyEveryChange,
        bool RollBackWhenPossible)
    {
        public static OptimizationSettings Default { get; } = new(
            AutomaticOptimizationEnabled: true,
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
