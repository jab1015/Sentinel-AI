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
        private const string LegacyRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SettingsKeyPath = @"Software\Modern Methods\Sentinel AI";
        private const string LegacyValueName = "Sentinel AI";
        private const string StartupPreferenceValueName = "StartWithWindows";
        private const string StartupTaskId = "SentinelStartupTask";

        public StartupRegistrationResult EnsureRegisteredAndVerify()
        {
            RemoveLegacyRunRegistration();
            try
            {
                StartupTask task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                if (!GetUserStartupPreference())
                {
                    if (task.State == StartupTaskState.Enabled) task.Disable();
                    return new(false, false, "Startup is disabled by the user.");
                }

                return task.State switch
                {
                    StartupTaskState.Enabled => new(true, false, "Sentinel AI packaged startup task is enabled and verified."),
                    StartupTaskState.DisabledByUser => new(false, false, "Windows reports that startup was disabled by the user. Sentinel will not override that choice."),
                    StartupTaskState.DisabledByPolicy => new(false, false, "Windows policy currently prevents Sentinel AI from starting automatically."),
                    StartupTaskState.Disabled => EnablePackagedTask(task),
                    _ => new(false, false, $"Sentinel AI startup state is {task.State}; automatic startup was not assumed to be enabled.")
                };
            }
            catch (Exception ex)
            {
                return new(false, false, $"Packaged startup registration could not be verified ({ex.GetType().Name}).");
            }
        }

        public StartupRegistrationResult SetStartupEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? settingsKey = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
                if (settingsKey is null)
                    return new(false, false, "Sentinel could not save the startup preference.");

                settingsKey.SetValue(StartupPreferenceValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
                RemoveLegacyRunRegistration();

                StartupTask task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                if (!enabled)
                {
                    bool changed = task.State == StartupTaskState.Enabled;
                    if (changed) task.Disable();
                    return new(false, changed, changed
                        ? "Sentinel AI packaged startup was disabled."
                        : "Startup was already disabled.");
                }

                if (task.State == StartupTaskState.DisabledByUser)
                    return new(false, false, "Windows reports that you disabled Sentinel AI startup in system settings. Re-enable it there before Sentinel can start automatically.");
                if (task.State == StartupTaskState.DisabledByPolicy)
                    return new(false, false, "Windows policy prevents Sentinel AI from starting automatically.");
                if (task.State == StartupTaskState.Enabled)
                    return new(true, false, "Sentinel AI packaged startup task is already enabled.");

                return EnablePackagedTask(task);
            }
            catch (Exception ex)
            {
                return new(false, false, $"Sentinel could not update the packaged startup task ({ex.GetType().Name}).");
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
            RemoveLegacyRunRegistration();
            try
            {
                StartupTask task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                return task.State == StartupTaskState.Enabled;
            }
            catch
            {
                return false;
            }
        }

        public bool EnsureRegistered() => EnsureRegisteredAndVerify().Registered;

        private static StartupRegistrationResult EnablePackagedTask(StartupTask task)
        {
            StartupTaskState state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            return state == StartupTaskState.Enabled
                ? new(true, true, "Sentinel AI packaged startup task was enabled and verified.")
                : new(false, false, state == StartupTaskState.DisabledByUser
                    ? "Windows did not enable startup because the user disabled it in system settings."
                    : $"Windows did not enable Sentinel AI startup. Current state: {state}.");
        }

        private static void RemoveLegacyRunRegistration()
        {
            try
            {
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: true);
                if (runKey?.GetValue(LegacyValueName) is not null)
                    runKey.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
            catch
            {
                // The legacy value is never treated as a successful startup mechanism.
            }
        }

        public sealed record StartupRegistrationResult(bool Registered, bool Repaired, string Summary);
    }
}
