/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Common escalation boundary for unresolved Sentinel investigations.
    /// Local verified evidence always remains authoritative. External information is
    /// accepted only when an approved source contains terms that materially match the
    /// current investigation. Cloud AI may interpret unresolved evidence only after
    /// local and authoritative methods have run, and it can never authorize repair.
    /// </summary>
    public sealed class ExternalInvestigationGateway
    {
        private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
        private const int MaxBodyCharacters = 500_000;
        private readonly InvestigationCache _cache = new();
        private readonly SmartSentinelAiCoordinator _aiCoordinator = new();

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

            IReadOnlyList<string> evidenceTerms = BuildEvidenceTerms(question, snapshot, topic);
            IReadOnlyList<TrustedSource> sources = SourcesFor(topic);
            List<ExternalSourceEvidence> reached = new();
            List<ExternalSourceEvidence> matched = new();

            using HttpClient client = new() { Timeout = NetworkTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelAI/1.0");

            foreach (TrustedSource source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, source.Uri);
                    using HttpResponseMessage response = await client.SendAsync(
                        request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) continue;

                    string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (body.Length > MaxBodyCharacters) body = body[..MaxBodyCharacters];
                    string searchable = NormalizeWebText(body);
                    IReadOnlyList<string> matches = evidenceTerms
                        .Where(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToArray();

                    ExternalSourceEvidence evidence = new(
                        source.Name, source.Uri, source.Authority, true, matches.Count > 0, matches);
                    reached.Add(evidence);
                    if (evidence.MatchedCurrentEvidence) matched.Add(evidence);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                catch { }
            }

            ExternalInvestigationResult result;
            if (reached.Count == 0)
            {
                result = new ExternalInvestigationResult(
                    topic, false, 0,
                    "Sentinel could not reach an approved authoritative source. No external conclusion was accepted and no change was made.",
                    Array.Empty<ExternalSourceEvidence>(), true, false, Array.Empty<string>());
            }
            else if (matched.Count == 0)
            {
                result = new ExternalInvestigationResult(
                    topic, false, 0,
                    $"Sentinel reached {reached.Count} approved authoritative source(s), but none contained enough information matching the current verified evidence. Sentinel did not accept an external conclusion.",
                    reached, true, false, Array.Empty<string>());
            }
            else
            {
                string[] matchedTerms = matched.SelectMany(x => x.MatchedTerms)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
                int sourceAuthority = matched.Max(x => x.Authority);
                int corroborationBonus = Math.Min(8, (matched.Count - 1) * 4);
                int termBonus = Math.Min(7, matchedTerms.Length);
                int confidence = Math.Min(95, Math.Max(60, sourceAuthority - 15 + corroborationBonus + termBonus));

                result = new ExternalInvestigationResult(
                    topic, true, confidence,
                    $"Sentinel found authoritative external material that matches {matchedTerms.Length} term(s) from the current verified {topic} evidence across {matched.Count} approved source(s). This supports further investigation, but it does not by itself prove a diagnosis or authorize a repair.",
                    reached, true, false, matchedTerms);
            }

            if (result.RequiresAiEscalation)
            {
                bool highRisk = topic.Equals("security", StringComparison.OrdinalIgnoreCase) ||
                                topic.Equals("firewall", StringComparison.OrdinalIgnoreCase);
                bool highComplexity = result.Sources.Count > 1 && !result.Verified;

                AiEscalationContext aiContext = new(
                    LocalEvidenceAvailable: evidenceTerms.Count > 0,
                    LocalEvidenceInsufficient: true,
                    LocalConclusionVerified: false,
                    CachedVerifiedFindingAvailable: false,
                    ExternalResearchApplicable: true,
                    AuthoritativeResearchAttempted: true,
                    AuthoritativeExternalConclusionVerified: result.Verified,
                    NeedsInterpretation: true,
                    NeedsUserExplanation: true,
                    HighComplexity: highComplexity,
                    HighRisk: highRisk);

                SmartAiResult ai = await _aiCoordinator.AnalyzeAsync(
                    "external-investigation",
                    question,
                    snapshot,
                    result,
                    aiContext,
                    cancellationToken).ConfigureAwait(false);

                if (ai.UsedCloudAi && !string.IsNullOrWhiteSpace(ai.Answer))
                {
                    string cacheNote = ai.FromCache ? " Reused a recent verified-evidence analysis with no new token request." : string.Empty;
                    result = result with
                    {
                        Summary = result.Summary +
                                  $" AI advisory ({ai.ConfidencePercent}% confidence): {ai.Answer.Trim()}" +
                                  " Sentinel treats this as interpretation only; any factual or actionable conclusion must still be verified against this computer." + cacheNote
                    };
                }
            }

            _cache.Set(cacheKey, result, CacheLifetime);
            return result;
        }

        private static IReadOnlyList<string> BuildEvidenceTerms(string question, SystemSnapshot snapshot, string topic)
        {
            string combined = string.Join(' ', new[]
            {
                question,
                snapshot.InvestigationReasonCode ?? string.Empty,
                snapshot.InvestigationConclusion ?? string.Empty,
                snapshot.InvestigationSummary ?? string.Empty,
                snapshot.GuidanceTitle ?? string.Empty,
                snapshot.GuidanceEvidence ?? string.Empty
            });

            HashSet<string> stop = new(StringComparer.OrdinalIgnoreCase)
            {
                "sentinel","windows","computer","current","verified","evidence","issue","problem","found",
                "what","when","where","which","with","from","that","this","have","does","could","would",
                "about","your","there","their","them","then","than","into","still","need","needs","attention"
            };

            List<string> terms = Regex.Matches(combined.ToLowerInvariant(), @"[a-z0-9][a-z0-9._-]{2,}")
                .Select(m => m.Value)
                .Where(x => !stop.Contains(x) && !int.TryParse(x, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Length)
                .Take(18)
                .ToList();

            if (!terms.Contains(topic, StringComparer.OrdinalIgnoreCase)) terms.Add(topic);
            return terms;
        }

        private static string NormalizeWebText(string html)
        {
            string withoutScripts = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string withoutTags = Regex.Replace(withoutScripts, @"<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim().ToLowerInvariant();
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

    public sealed record ExternalSourceEvidence(
        string SourceName,
        string Uri,
        int Authority,
        bool Reached,
        bool MatchedCurrentEvidence,
        IReadOnlyList<string> MatchedTerms);

    public sealed record ExternalInvestigationResult(
        string Topic,
        bool Verified,
        int ConfidencePercent,
        string Summary,
        IReadOnlyList<ExternalSourceEvidence> Sources,
        bool RequiresAiEscalation,
        bool FromCache,
        IReadOnlyList<string> MatchedTerms)
    {
        public static ExternalInvestigationResult NotVerified(string topic, string summary) =>
            new(topic, false, 0, summary, Array.Empty<ExternalSourceEvidence>(), false, false, Array.Empty<string>());
    }
}
