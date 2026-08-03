/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using Windows.ApplicationModel;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Registers and verifies packaged Sentinel for per-user Windows sign-in startup.
    /// The package family name/AUMID command survives package version updates.
    /// </summary>
    public sealed class WindowsStartupRegistrationService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Sentinel AI";
        private const string ApplicationId = "App";

        public StartupRegistrationResult EnsureRegisteredAndVerify()
        {
            try
            {
                string familyName = Package.Current.Id.FamilyName;
                if (string.IsNullOrWhiteSpace(familyName))
                    return new(false, false, "The installed package identity is unavailable.");

                string appUserModelId = $"{familyName}!{ApplicationId}";
                string expectedCommand = $"explorer.exe \"shell:AppsFolder\\{appUserModelId}\"";

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
                        ? "Sentinel AI startup registration was repaired with the Windows packaged-app launch command and verified."
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

        public bool EnsureRegistered() => EnsureRegisteredAndVerify().Registered;

        public sealed record StartupRegistrationResult(bool Registered, bool Repaired, string Summary);
    }
}
