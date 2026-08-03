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
    /// Registers the packaged Sentinel application for per-user Windows sign-in startup.
    /// The registration uses the package family name/AUMID so it survives package version updates.
    /// </summary>
    public sealed class WindowsStartupRegistrationService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Sentinel AI";
        private const string ApplicationId = "App";

        public bool EnsureRegistered()
        {
            try
            {
                string familyName = Package.Current.Id.FamilyName;
                if (string.IsNullOrWhiteSpace(familyName))
                {
                    return false;
                }

                string command = $"explorer.exe shell:AppsFolder\\{familyName}!{ApplicationId}";
                using RegistryKey? runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (runKey is null)
                {
                    return false;
                }

                string? existing = runKey.GetValue(ValueName) as string;
                if (!string.Equals(existing, command, StringComparison.OrdinalIgnoreCase))
                {
                    runKey.SetValue(ValueName, command, RegistryValueKind.String);
                }

                return true;
            }
            catch (InvalidOperationException)
            {
                // Package.Current is unavailable when the app is run unpackaged during development.
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }
    }
}
