/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Common escalation boundary for unresolved Sentinel investigations.
    /// Local verified evidence always remains authoritative. External information is
    /// advisory until it is corroborated and must never manufacture a repair outcome.
    /// </summary>
    public sealed class ExternalInvestigationGateway
    {
        private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
        private readonly InvestigationCache _cache = new();

        public async Task<ExternalInvestigationResult> InvestigateAsync(
            string question,
            SystemSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            string topic = Classify(question, snapshot);
            string cacheKey = $"external:{topic}:{Normalize(question)}";
            if (_cache.TryGet(cacheKey, out ExternalInvestigationResult? cached) && cached is not null)
                return cached with { FromCache = true };

            IReadOnlyList<TrustedSource> sources = SourcesFor(topic);
            if (sources.Count == 0)
                return ExternalInvestigationResult.NotVerified(topic,
                    "Sentinel does not have an approved authoritative external source for this investigation yet.");

            List<ExternalSourceEvidence> reached = new();
            using HttpClient client = new() { Timeout = NetworkTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelAI/1.0");

            foreach (TrustedSource source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, source.Uri);
                    using HttpResponseMessage response = await client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        reached.Add(new ExternalSourceEvidence(source.Name, source.Uri, source.Authority, true));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Per-source timeout: continue to the next approved authority.
                }
                catch
                {
                    // Network/source failure is evidence of unavailability, not permission to guess.
                }
            }

            ExternalInvestigationResult result;
            if (reached.Count == 0)
            {
                result = ExternalInvestigationResult.NotVerified(topic,
                    "Sentinel could not reach an approved authoritative source. No external conclusion was accepted and no change was made.");
            }
            else
            {
                int confidence = Math.Min(90, reached.Max(x => x.Authority));
                result = new ExternalInvestigationResult(
                    Topic: topic,
                    Verified: true,
                    ConfidencePercent: confidence,
                    Summary: $"Sentinel reached {reached.Count} approved authoritative source(s) for this {topic} investigation. Source availability is verified; a specific factual conclusion still requires evidence matching the current computer before Sentinel may act on it.",
                    Sources: reached,
                    RequiresAiEscalation: true,
                    FromCache: false);
            }

            _cache.Set(cacheKey, result, CacheLifetime);
            return result;
        }

        private static string Classify(string question, SystemSnapshot snapshot)
        {
            string value = (question + " " + snapshot.InvestigationReasonCode + " " +
                            snapshot.InvestigationConclusion + " " + snapshot.InvestigationSummary).ToLowerInvariant();
            if (ContainsAny(value, "driver", "device manager", "hardware id")) return "driver";
            if (ContainsAny(value, "network", "internet", "dns", "wifi", "ethernet", "connection")) return "network";
            if (ContainsAny(value, "defender", "malware", "virus", "spyware", "threat", "security")) return "security";
            if (ContainsAny(value, "firewall", "port", "blocked connection")) return "firewall";
            if (ContainsAny(value, "windows update", "update error", "kb")) return "windows-update";
            if (ContainsAny(value, "process", "service", "event log", "error code", "exception")) return "windows-diagnostics";
            return "windows-general";
        }

        private static IReadOnlyList<TrustedSource> SourcesFor(string topic) => topic switch
        {
            "driver" => new[]
            {
                new TrustedSource("Microsoft Update Catalog", "https://www.catalog.update.microsoft.com/", 95),
                new TrustedSource("Microsoft Learn - Windows drivers", "https://learn.microsoft.com/windows-hardware/drivers/", 95)
            },
            "network" => new[]
            {
                new TrustedSource("Microsoft Learn - Windows networking", "https://learn.microsoft.com/windows-server/networking/", 95),
                new TrustedSource("Microsoft Support", "https://support.microsoft.com/windows", 90)
            },
            "security" => new[]
            {
                new TrustedSource("Microsoft Security Intelligence", "https://www.microsoft.com/wdsi", 98),
                new TrustedSource("Microsoft Learn - Defender", "https://learn.microsoft.com/defender-endpoint/", 95)
            },
            "firewall" => new[]
            {
                new TrustedSource("Microsoft Learn - Windows Firewall", "https://learn.microsoft.com/windows/security/operating-system-security/network-security/windows-firewall/", 95)
            },
            "windows-update" => new[]
            {
                new TrustedSource("Windows release health", "https://learn.microsoft.com/windows/release-health/", 98),
                new TrustedSource("Microsoft Update Catalog", "https://www.catalog.update.microsoft.com/", 95)
            },
            "windows-diagnostics" => new[]
            {
                new TrustedSource("Microsoft Learn", "https://learn.microsoft.com/windows/", 95),
                new TrustedSource("Microsoft Support", "https://support.microsoft.com/windows", 90)
            },
            _ => new[]
            {
                new TrustedSource("Microsoft Learn - Windows", "https://learn.microsoft.com/windows/", 95),
                new TrustedSource("Microsoft Support", "https://support.microsoft.com/windows", 90)
            }
        };

        private static bool ContainsAny(string value, params string[] terms) =>
            terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

        private static string Normalize(string value) =>
            string.Join(' ', value.Trim().ToLowerInvariant().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private sealed record TrustedSource(string Name, string Uri, int Authority);
    }

    public sealed record ExternalSourceEvidence(string SourceName, string Uri, int Authority, bool Reached);

    public sealed record ExternalInvestigationResult(
        string Topic,
        bool Verified,
        int ConfidencePercent,
        string Summary,
        IReadOnlyList<ExternalSourceEvidence> Sources,
        bool RequiresAiEscalation,
        bool FromCache)
    {
        public static ExternalInvestigationResult NotVerified(string topic, string summary) =>
            new(topic, false, 0, summary, Array.Empty<ExternalSourceEvidence>(), false, false);
    }
}
