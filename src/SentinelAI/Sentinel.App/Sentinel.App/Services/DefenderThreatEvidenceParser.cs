/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Parses Microsoft Defender threat state without performing any system change.
    /// Only active Defender threats are considered. A Sentinel file-quarantine
    /// candidate requires an unresolved/non-executing detection, an exact file:_
    /// resource, and a file that still exists at that canonical path.
    /// </summary>
    public static class DefenderThreatEvidenceParser
    {
        private static readonly HashSet<int> UnresolvedThreatStatuses = new()
        {
            1,   // Detected
            101, // Clean failed
            102, // Quarantine failed
            103, // Remove failed
            104, // Allow failed
            105, // Abandoned
            107  // Block failed
        };

        public static DefenderThreatEvidenceSnapshot Parse(
            string? json,
            Func<string, bool>? fileExists = null)
        {
            fileExists ??= File.Exists;

            if (string.IsNullOrWhiteSpace(json))
                return DefenderThreatEvidenceSnapshot.Unavailable("Microsoft Defender threat evidence returned no data.");

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                bool available = TryBool(root, "Available", out bool parsedAvailable) && parsedAvailable;
                if (!available)
                    return DefenderThreatEvidenceSnapshot.Unavailable("Microsoft Defender threat evidence is unavailable.");

                if (!root.TryGetProperty("Threats", out JsonElement threatsElement) ||
                    threatsElement.ValueKind is not JsonValueKind.Array)
                {
                    return new DefenderThreatEvidenceSnapshot(
                        true, 0, false, 0, "None", 0, false, "None", 0, 0, false, null,
                        "Microsoft Defender reports no active threats.");
                }

                List<ParsedThreat> active = new();
                foreach (JsonElement threat in threatsElement.EnumerateArray())
                {
                    if (!TryBool(threat, "IsActive", out bool isActive) || !isActive)
                        continue;

                    long threatId = TryInt64(threat, "ThreatID");
                    string name = TryString(threat, "ThreatName") ?? $"Threat {threatId}";
                    int severity = TryInt32(threat, "SeverityID");
                    bool didExecute = TryBool(threat, "DidThreatExecute", out bool executed) && executed;
                    List<ParsedDetection> detections = ParseDetections(threat);
                    active.Add(new ParsedThreat(threatId, name, severity, didExecute, detections));
                }

                if (active.Count == 0)
                {
                    return new DefenderThreatEvidenceSnapshot(
                        true, 0, false, 0, "None", 0, false, "None", 0, 0, false, null,
                        "Microsoft Defender reports no active threats.");
                }

                ParsedThreat primary = active
                    .OrderByDescending(t => t.SeverityId)
                    .ThenByDescending(t => t.DidThreatExecute)
                    .First();

                Candidate? candidate = active
                    .OrderByDescending(t => t.SeverityId)
                    .ThenByDescending(t => t.DidThreatExecute)
                    .SelectMany(t => BuildCandidates(t, fileExists))
                    .OrderByDescending(c => c.LastStatusChangeUtc ?? DateTimeOffset.MinValue)
                    .FirstOrDefault();

                string summary = candidate is not null
                    ? $"Microsoft Defender reports {active.Count} active threat(s). Sentinel verified an exact existing file resource for {candidate.ThreatName} that remains eligible for approval-gated quarantine review."
                    : $"Microsoft Defender reports {active.Count} active threat(s), but Sentinel does not currently have an exact existing unresolved file resource that it can safely move into its own quarantine.";

                return new DefenderThreatEvidenceSnapshot(
                    EvidenceAvailable: true,
                    ActiveThreatCount: active.Count,
                    FileQuarantineCandidateAvailable: candidate is not null,
                    PrimaryThreatId: candidate?.ThreatId ?? primary.ThreatId,
                    PrimaryThreatName: candidate?.ThreatName ?? primary.ThreatName,
                    PrimaryThreatSeverityId: candidate?.SeverityId ?? primary.SeverityId,
                    PrimaryThreatDidExecute: candidate?.DidThreatExecute ?? primary.DidThreatExecute,
                    PrimaryThreatFilePath: candidate?.FilePath ?? "None",
                    PrimaryThreatStatusId: candidate?.ThreatStatusId ?? 0,
                    PrimaryThreatExecutionStatusId: candidate?.ExecutionStatusId ?? 0,
                    PrimaryThreatActionSuccess: candidate?.ActionSuccess ?? false,
                    PrimaryThreatDetectedAtUtc: candidate?.InitialDetectionUtc,
                    Summary: summary);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return DefenderThreatEvidenceSnapshot.Unavailable("Microsoft Defender threat evidence could not be parsed safely.");
            }
        }

        private static IEnumerable<Candidate> BuildCandidates(
            ParsedThreat threat,
            Func<string, bool> fileExists)
        {
            foreach (ParsedDetection detection in threat.Detections
                .OrderByDescending(d => d.LastStatusChangeUtc ?? d.InitialDetectionUtc ?? DateTimeOffset.MinValue))
            {
                if (!UnresolvedThreatStatuses.Contains(detection.ThreatStatusId))
                    continue;

                // Defender is actively remediating this detection. Do not race it.
                if (detection.ExecutionStatusId == 3)
                    continue;

                foreach (string resource in detection.Resources)
                {
                    if (!TryExactFileResource(resource, out string path))
                        continue;

                    bool exists;
                    try { exists = fileExists(path); }
                    catch { exists = false; }
                    if (!exists)
                        continue;

                    yield return new Candidate(
                        threat.ThreatId,
                        threat.ThreatName,
                        threat.SeverityId,
                        threat.DidThreatExecute,
                        path,
                        detection.ThreatStatusId,
                        detection.ExecutionStatusId,
                        detection.ActionSuccess,
                        detection.InitialDetectionUtc,
                        detection.LastStatusChangeUtc);
                }
            }
        }

        private static bool TryExactFileResource(string? resource, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(resource) ||
                !resource.StartsWith("file:_", StringComparison.OrdinalIgnoreCase))
                return false;

            string raw = resource[6..].Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            try
            {
                string canonical = Path.GetFullPath(raw);
                if (!Path.IsPathFullyQualified(canonical))
                    return false;
                path = canonical;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<ParsedDetection> ParseDetections(JsonElement threat)
        {
            List<ParsedDetection> result = new();
            if (!threat.TryGetProperty("Detections", out JsonElement detections) ||
                detections.ValueKind is not JsonValueKind.Array)
                return result;

            foreach (JsonElement detection in detections.EnumerateArray())
            {
                List<string> resources = new();
                if (detection.TryGetProperty("Resources", out JsonElement resourceElement) &&
                    resourceElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement resource in resourceElement.EnumerateArray())
                    {
                        if (resource.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(resource.GetString()))
                            resources.Add(resource.GetString()!);
                    }
                }

                result.Add(new ParsedDetection(
                    TryInt32(detection, "ThreatStatusID"),
                    TryInt32(detection, "CurrentThreatExecutionStatusID"),
                    TryBool(detection, "ActionSuccess", out bool actionSuccess) && actionSuccess,
                    TryDateTimeOffset(detection, "InitialDetectionTime"),
                    TryDateTimeOffset(detection, "LastThreatStatusChangeTime"),
                    resources));
            }

            return result;
        }

        private static string? TryString(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int TryInt32(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
        }

        private static long TryInt64(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
            return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : 0;
        }

        private static bool TryBool(JsonElement element, string property, out bool parsed)
        {
            parsed = false;
            if (!element.TryGetProperty(property, out JsonElement value)) return false;
            if (value.ValueKind == JsonValueKind.True) { parsed = true; return true; }
            if (value.ValueKind == JsonValueKind.False) { parsed = false; return true; }
            return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out parsed);
        }

        private static DateTimeOffset? TryDateTimeOffset(JsonElement element, string property)
        {
            string? value = TryString(element, property);
            return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;
        }

        private sealed record ParsedThreat(
            long ThreatId,
            string ThreatName,
            int SeverityId,
            bool DidThreatExecute,
            List<ParsedDetection> Detections);

        private sealed record ParsedDetection(
            int ThreatStatusId,
            int ExecutionStatusId,
            bool ActionSuccess,
            DateTimeOffset? InitialDetectionUtc,
            DateTimeOffset? LastStatusChangeUtc,
            List<string> Resources);

        private sealed record Candidate(
            long ThreatId,
            string ThreatName,
            int SeverityId,
            bool DidThreatExecute,
            string FilePath,
            int ThreatStatusId,
            int ExecutionStatusId,
            bool ActionSuccess,
            DateTimeOffset? InitialDetectionUtc,
            DateTimeOffset? LastStatusChangeUtc);

        public sealed record DefenderThreatEvidenceSnapshot(
            bool EvidenceAvailable,
            int ActiveThreatCount,
            bool FileQuarantineCandidateAvailable,
            long PrimaryThreatId,
            string PrimaryThreatName,
            int PrimaryThreatSeverityId,
            bool PrimaryThreatDidExecute,
            string PrimaryThreatFilePath,
            int PrimaryThreatStatusId,
            int PrimaryThreatExecutionStatusId,
            bool PrimaryThreatActionSuccess,
            DateTimeOffset? PrimaryThreatDetectedAtUtc,
            string Summary)
        {
            public static DefenderThreatEvidenceSnapshot Unavailable(string summary) =>
                new(false, 0, false, 0, "None", 0, false, "None", 0, 0, false, null, summary);
        }
    }
}
