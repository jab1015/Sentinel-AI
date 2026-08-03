/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using Windows.ApplicationModel;

namespace Sentinel.App.Services
{
    public sealed class WindowsStartupRegistrationService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SettingsKeyPath = @"Software\Modern Methods\Sentinel AI";
        private const string ValueName = "Sentinel AI";
        private const string StartupPreferenceValueName = "StartWithWindows";
        private const string ApplicationId = "App";

        public StartupRegistrationResult EnsureRegisteredAndVerify()
        {
            if (!GetUserStartupPreference())
            {
                RemoveRunRegistration();
                return new(false, false, "Startup is disabled by the user.");
            }

            return RegisterAndVerify();
        }

        public StartupRegistrationResult SetStartupEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? settingsKey = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
                if (settingsKey is null)
                    return new(false, false, "Sentinel could not save the startup preference.");

                settingsKey.SetValue(StartupPreferenceValueName, enabled ? 1 : 0, RegistryValueKind.DWord);

                if (!enabled)
                {
                    bool removed = RemoveRunRegistration();
                    return new(false, removed, removed
                        ? "Sentinel will no longer start when you sign in."
                        : "Startup was already disabled.");
                }

                return RegisterAndVerify();
            }
            catch (UnauthorizedAccessException)
            {
                return new(IsStartupRegistered(), false, "Windows denied access to the current user's startup settings.");
            }
            catch (System.Security.SecurityException)
            {
                return new(IsStartupRegistered(), false, "Windows security policy prevented the startup setting from being changed.");
            }
        }

        public bool GetUserStartupPreference()
        {
            try
            {
                using RegistryKey? settingsKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
                object? value = settingsKey?.GetValue(StartupPreferenceValueName);
                return value is null || Convert.ToInt32(value) != 0;
            }
            catch
            {
                return true;
            }
        }

        public bool IsStartupRegistered()
        {
            try
            {
                string expectedCommand = GetExpectedCommand();
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                string? existing = runKey?.GetValue(ValueName) as string;
                return string.Equals(existing, expectedCommand, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public bool EnsureRegistered() => EnsureRegisteredAndVerify().Registered;

        private StartupRegistrationResult RegisterAndVerify()
        {
            try
            {
                string expectedCommand = GetExpectedCommand();
                using RegistryKey? runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (runKey is null)
                    return new(false, false, "Windows startup registration could not be opened.");

                string? existing = runKey.GetValue(ValueName) as string;
                bool changed = !string.Equals(existing, expectedCommand, StringComparison.OrdinalIgnoreCase);
                if (changed)
                    runKey.SetValue(ValueName, expectedCommand, RegistryValueKind.String);

                string? verified = runKey.GetValue(ValueName) as string;
                bool registered = string.Equals(verified, expectedCommand, StringComparison.OrdinalIgnoreCase);
                return registered
                    ? new(true, changed, changed
                        ? "Sentinel AI startup registration was repaired and verified."
                        : "Sentinel AI startup registration is present and verified.")
                    : new(false, changed, "Windows did not retain Sentinel AI startup registration.");
            }
            catch (InvalidOperationException)
            {
                return new(false, false, "Startup registration is unavailable while Sentinel is running without installed package identity.");
            }
            catch (UnauthorizedAccessException)
            {
                return new(false, false, "Windows denied access to the current user's startup registration.");
            }
            catch (System.Security.SecurityException)
            {
                return new(false, false, "Windows security policy prevented startup registration.");
            }
        }

        private static string GetExpectedCommand()
        {
            string familyName = Package.Current.Id.FamilyName;
            if (string.IsNullOrWhiteSpace(familyName))
                throw new InvalidOperationException("The installed package identity is unavailable.");

            return $"explorer.exe shell:AppsFolder\\{familyName}!{ApplicationId}";
        }

        private static bool RemoveRunRegistration()
        {
            try
            {
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (runKey?.GetValue(ValueName) is null)
                    return false;

                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public sealed record StartupRegistrationResult(bool Registered, bool Repaired, string Summary);
    }
}
