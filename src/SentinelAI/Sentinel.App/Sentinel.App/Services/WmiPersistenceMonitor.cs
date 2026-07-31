/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reviews permanent WMI event subscriptions as persistence evidence.
    /// A finding is not actionable by itself and must be correlated with
    /// process, command-line, registry, or network evidence.
    /// </summary>
    public sealed class WmiPersistenceMonitor
    {
        public WmiPersistenceSnapshot GetSnapshot()
        {
            try
            {
                string script =
                    "$ns='root/subscription';" +
                    "$filters=@(Get-CimInstance -Namespace $ns -ClassName __EventFilter -ErrorAction SilentlyContinue);" +
                    "$cmd=@(Get-CimInstance -Namespace $ns -ClassName CommandLineEventConsumer -ErrorAction SilentlyContinue);" +
                    "$scriptConsumers=@(Get-CimInstance -Namespace $ns -ClassName ActiveScriptEventConsumer -ErrorAction SilentlyContinue);" +
                    "$bindings=@(Get-CimInstance -Namespace $ns -ClassName __FilterToConsumerBinding -ErrorAction SilentlyContinue);" +
                    "[pscustomobject]@{Filters=$filters;CommandConsumers=$cmd;ScriptConsumers=$scriptConsumers;Bindings=$bindings}|ConvertTo-Json -Depth 5 -Compress";

                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return Empty("WMI persistence data was unavailable.");
                }

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(10000) || process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    TryKill(process);
                    return Empty("WMI persistence data was unavailable.");
                }

                using JsonDocument document = JsonDocument.Parse(output);
                JsonElement root = document.RootElement;

                int filterCount = CountItems(root, "Filters");
                int commandConsumerCount = CountItems(root, "CommandConsumers");
                int scriptConsumerCount = CountItems(root, "ScriptConsumers");
                int bindingCount = CountItems(root, "Bindings");

                List<WmiFinding> findings = new();
                ReviewCommandConsumers(root, findings);
                ReviewScriptConsumers(root, findings);

                WmiFinding? primary = findings.Count > 0 ? findings[0] : null;
                return new WmiPersistenceSnapshot(
                    filterCount,
                    commandConsumerCount + scriptConsumerCount,
                    bindingCount,
                    findings.Count,
                    primary?.Name ?? "None",
                    primary?.ConsumerType ?? "None",
                    primary?.Reason ?? "No unusual permanent WMI event consumers were detected.");
            }
            catch
            {
                return Empty("WMI persistence data was unavailable.");
            }
        }

        private static void ReviewCommandConsumers(JsonElement root, List<WmiFinding> findings)
        {
            foreach (JsonElement item in EnumerateItems(root, "CommandConsumers"))
            {
                string name = GetString(item, "Name");
                string command = GetString(item, "CommandLineTemplate");
                string executable = GetString(item, "ExecutablePath");
                string combined = $"{executable} {command}".Trim();

                if (string.IsNullOrWhiteSpace(combined))
                {
                    continue;
                }

                findings.Add(new WmiFinding(
                    string.IsNullOrWhiteSpace(name) ? "Unnamed consumer" : name,
                    "CommandLineEventConsumer",
                    $"A permanent WMI consumer can start this command: {Shorten(combined)}"));
            }
        }

        private static void ReviewScriptConsumers(JsonElement root, List<WmiFinding> findings)
        {
            foreach (JsonElement item in EnumerateItems(root, "ScriptConsumers"))
            {
                string name = GetString(item, "Name");
                string engine = GetString(item, "ScriptingEngine");
                string scriptText = GetString(item, "ScriptText");

                findings.Add(new WmiFinding(
                    string.IsNullOrWhiteSpace(name) ? "Unnamed consumer" : name,
                    "ActiveScriptEventConsumer",
                    $"A permanent WMI consumer can execute {Fallback(engine, "a script engine")} code: {Shorten(scriptText)}"));
            }
        }

        private static IEnumerable<JsonElement> EnumerateItems(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined)
            {
                yield break;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    yield return item;
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                yield return value;
            }
        }

        private static int CountItems(JsonElement root, string propertyName)
        {
            int count = 0;
            foreach (JsonElement _ in EnumerateItems(root, propertyName))
            {
                count++;
            }

            return count;
        }

        private static string GetString(JsonElement item, string propertyName)
        {
            return item.TryGetProperty(propertyName, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string Fallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string Shorten(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "content unavailable";
            }

            string redacted = value.Replace(
                Environment.UserName,
                "<user>",
                StringComparison.OrdinalIgnoreCase);

            return redacted.Length <= 180 ? redacted : redacted[..177] + "...";
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static WmiPersistenceSnapshot Empty(string reason) =>
            new(0, 0, 0, 0, "None", "None", reason);

        private sealed record WmiFinding(string Name, string ConsumerType, string Reason);

        public sealed record WmiPersistenceSnapshot(
            int FilterCount,
            int ConsumerCount,
            int BindingCount,
            int ReviewFindingCount,
            string PrimaryConsumerName,
            string PrimaryConsumerType,
            string PrimaryReason);
    }
}
