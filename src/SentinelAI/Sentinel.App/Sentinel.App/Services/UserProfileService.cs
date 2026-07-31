/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Windows.Storage;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Stores Sentinel's preferred greeting name in the current Windows user's
    /// local application settings. Each Windows profile therefore keeps its own
    /// preferred name without sharing it with other users on the computer.
    /// </summary>
    public sealed class UserProfileService
    {
        private const string PreferredNameKey = "PreferredGreetingName";

        public string GetPreferredName()
        {
            object? value = ApplicationData.Current.LocalSettings.Values[PreferredNameKey];
            return value as string ?? string.Empty;
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

            ApplicationData.Current.LocalSettings.Values[PreferredNameKey] = normalized;
        }
    }
}
