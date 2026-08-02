/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Stores Sentinel's preferred greeting name in the current Windows user's
    /// local application data. Each Windows profile therefore keeps its own
    /// preferred name without requiring packaged-app identity.
    /// </summary>
    public sealed class UserProfileService
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Modern Methods",
            "Sentinel AI");

        private static readonly string PreferredNamePath = Path.Combine(
            SettingsDirectory,
            "preferred-greeting-name.txt");

        public string GetPreferredName()
        {
            try
            {
                if (!File.Exists(PreferredNamePath))
                {
                    return string.Empty;
                }

                return File.ReadAllText(PreferredNamePath).Trim();
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        public bool HasPreferredName() =>
            !string.IsNullOrWhiteSpace(GetPreferredName());

        public string GetSuggestedName()
        {
            string windowsUserName = Environment.UserName?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(windowsUserName)
                ? "there"
                : windowsUserName;
        }

        public void SavePreferredName(string name)
        {
            string normalized = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(PreferredNamePath, normalized);
        }
    }
}
