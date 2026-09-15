/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class ExternalInvestigationGateway
    {
        private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
        private const int MaxBodyCharacters = 500_000;
        private readonly InvestigationCache _cache = new();
        private readonly SmartSentinelAiCoordinator _aiCoordinator = new();
        private readonly DriverDiagnosticEvidenceCollector _driverEvidenceCollector = new();
        private readonly StoreSubscriptionService _subscriptionService = new();
        private readonly MicrosoftLearnResearchClient _microsoftLearn = new();

        public async Task<ExternalInvestigationResult> InvestigateAsync(string question, SystemSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            string topic = Classify(question, snapshot);
            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
                return ExternalInvestigationResult.SubscriptionRequired(topic, "Sentinel answered from free local evidence and Basic AI when useful. An active subscription is required to investigate current approved external sources or use Advanced AI.");

            string evidenceFingerprint = BuildEvidenceFingerprint(question, snapshot, topic);
            string cacheKey = $"external:{topic}:{evidenceFingerprint}";
            if (_cache.TryGet(cacheKey, out ExternalInvestigationResult? cached) && cached is not null)
                return cached with { FromCache = true };

            string supplementalEvidence = string.Empty;
            if (topic.Equals("driver", StringComparison.OrdinalIgnoreCase))
            {
                string deviceName = ExtractLikelyDriverDeviceName(snapshot);
                DriverDiagnosticEvidence diagnostics = await _driverEvidenceCollector.CollectAsync(deviceName).ConfigureAwait(false);
                supplementalEvidence = diagnostics.ToInvestigationSummary();
            }

            IReadOnlyList<string> evidenceTerms = BuildEvidenceTerms(question, snapshot);
            List<ExternalSourceEvidence> reached = new();
            List<ExternalSourceEvidence> relevant = new();

            MicrosoftLearnResearchResult learn = await _microsoftLearn.SearchAsync(question, cancellationToken).ConfigureAwait(false);
            foreach (MicrosoftLearnDocument document in learn.Documents)
            {
                string[] matches = evidenceTerms
                    .Where(term => document.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray();

                string matchedTerm = matches.FirstOrDefault() ?? "semantic-search";
                ExternalSourceEvidence evidence = new(
                    $"Microsoft Learn — {document.Title}",
                    document.ContentUrl,
                    98,
                    true,
                    matches.Length > 0,
                    matches,
                    new[] { new ExternalResearchPassage(matchedTerm, document.Content) });
                reached.Add(evidence);
                relevant.Add(evidence);
            }

            // Microsoft Learn MCP is the preferred source because it performs semantic search
            // over current official documentation. The older pinned-page reader remains only as
            // a bounded fallback if MCP search is unavailable or yields no usable documents.
            if (relevant.Count == 0)
            {
                IReadOnlyList<TrustedSource> sources = SourcesFor(topic);
                using HttpClientHandler handler = new()
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
                using HttpClient client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelAI/1.0");

                foreach (TrustedSource source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (!TryCreatePinnedHttpsUri(source.Uri, out Uri? expectedUri) || expectedUri is null)
                            continue;

                        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        deadline.CancelAfter(NetworkTimeout);
                        using HttpRequestMessage request = new(HttpMethod.Get, expectedUri);
                        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode || !ResponseMatchesExpectedAuthority(response, expectedUri))
                            continue;

                        string? body = await ReadBoundedBodyAsync(response, deadline.Token).ConfigureAwait(false);
                        if (body is null) continue;

                        string searchable = NormalizeWebText(body);
                        IReadOnlyList<ExternalResearchPassage> passages =
                            ExternalResearchProvenancePolicy.ExtractPassages(searchable, evidenceTerms);
                        IReadOnlyList<string> matches = passages
                            .Select(p => p.MatchedTerm)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                        ExternalSourceEvidence evidence = new(
                            source.Name,
                            source.Uri,
                            source.Authority,
                            true,
                            passages.Count > 0,
                            matches,
                            passages);
                        reached.Add(evidence);
                        if (passages.Count > 0) relevant.Add(evidence);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    catch { }
                }
            }

            ExternalInvestigationResult result;
            if (reached.Count == 0)
            {
                string reason = learn.Available
                    ? "Sentinel searched Microsoft Learn and the approved fallback sources but found no usable authoritative material for this question."
                    : $"{learn.Reason} Sentinel also could not reach a usable approved fallback source.";
                result = new ExternalInvestigationResult(topic, false, 0,
                    reason + " No external conclusion was accepted and no change was made.",
                    Array.Empty<ExternalSourceEvidence>(), true, false, Array.Empty<string>());
            }
            else if (relevant.Count == 0)
            {
                result = new ExternalInvestigationResult(topic, false, 0,
                    $"Sentinel reached {reached.Count} approved authoritative source(s), but did not find attributable material relevant enough to the current question and evidence. No external conclusion was accepted.",
                    reached, true, false, Array.Empty<string>());
            }
            else
            {
                string[] matchedTerms = relevant.SelectMany(x => x.MatchedTerms).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
                bool usedLearn = relevant.Any(source => source.SourceName.StartsWith("Microsoft Learn", StringComparison.OrdinalIgnoreCase));
                string method = usedLearn
                    ? "searched current Microsoft Learn documentation"
                    : "checked pinned approved authoritative pages";
                result = new ExternalInvestigationResult(topic, false, 0,
                    $"Sentinel {method} and retrieved {relevant.Count} attributable result(s) relevant to the question. These passages are authoritative external guidance, but they are not proof of this computer's local state or proof that Sentinel performed a security action.",
                    reached, true, false, matchedTerms);
            }

            if (result.RequiresAiEscalation)
            {
                bool highRisk = topic.Equals("security", StringComparison.OrdinalIgnoreCase) || topic.Equals("firewall", StringComparison.OrdinalIgnoreCase);
                bool highComplexity = result.Sources.Count > 1;
                AiEscalationContext aiContext = new(
                    LocalEvidenceAvailable: evidenceTerms.Count > 0 || !string.IsNullOrWhiteSpace(supplementalEvidence),
                    LocalEvidenceInsufficient: true,
                    LocalConclusionVerified: false,
                    CachedVerifiedFindingAvailable: false,
                    ExternalResearchApplicable: true,
                    AuthoritativeResearchAttempted: true,
                    AuthoritativeExternalConclusionVerified: false,
                    NeedsInterpretation: true,
                    NeedsUserExplanation: true,
                    HighComplexity: highComplexity,
                    HighRisk: highRisk);

                SmartAiResult ai = await _aiCoordinator.AnalyzeAsync("external-investigation", question, snapshot, result, aiContext, cancellationToken, supplementalEvidence).ConfigureAwait(false);
                if (ai.UsedCloudAi)
                {
                    string cacheNote = ai.FromCache ? " A recent analysis for identical redacted evidence was reused without another provider request." : string.Empty;
                    string advisoryOutcome = ai.RequiresMoreEvidence
                        ? "The AI advisory also indicated that more verified evidence is needed before Sentinel can make a stronger conclusion."
                        : "The AI advisory interpreted the supplied local and authoritative evidence, but it is not permitted to assert that Sentinel performed or verified a security action.";
                    result = result with
                    {
                        Summary = result.Summary + $" Sentinel's AI advisory analysis completed ({ai.ConfidencePercent}% heuristic confidence). {advisoryOutcome}" + cacheNote,
                        AiAdvisoryUsed = true,
                        AiRequiresMoreEvidence = ai.RequiresMoreEvidence
                    };
                }
            }

            _cache.Set(cacheKey, result, CacheLifetime);
            return result;
        }

        private static async Task<string?> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken token)
        {
            if (response.Content.Headers.ContentLength is long length && length > MaxBodyCharacters * 4L)
                return null;

            await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: false);
            char[] buffer = new char[8192];
            StringBuilder builder = new(Math.Min(MaxBodyCharacters, 32_768));
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read == 0) break;
                if (builder.Length + read > MaxBodyCharacters) return null;
                builder.Append(buffer, 0, read);
            }
            return builder.ToString();
        }

        private static bool TryCreatePinnedHttpsUri(string uri, out Uri? expectedUri)
        {
            expectedUri = null;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) ||
                !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(parsed.Host) ||
                !string.IsNullOrEmpty(parsed.UserInfo))
                return false;

            expectedUri = parsed;
            return true;
        }

        private static bool ResponseMatchesExpectedAuthority(HttpResponseMessage response, Uri expectedUri)
        {
            return response.RequestMessage?.RequestUri is Uri actualUri &&
                   actualUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                   actualUri.Host.Equals(expectedUri.Host, StringComparison.OrdinalIgnoreCase) &&
                   actualUri.Port == expectedUri.Port;
        }

        private static string BuildEvidenceFingerprint(string question, SystemSnapshot snapshot, string topic)
        {
            string material = string.Join("\n", new[]
            {
                Normalize(question), topic,
                snapshot.Timestamp.ToUniversalTime().ToString("yyyyMMddHHmm"),
                snapshot.InvestigationReasonCode ?? string.Empty,
                snapshot.InvestigationConclusion ?? string.Empty,
                snapshot.InvestigationSummary ?? string.Empty,
                snapshot.GuidanceEvidence ?? string.Empty,
                snapshot.PrimaryFlaggedProcessName ?? string.Empty,
                snapshot.PrimaryFlaggedConnectionRemoteEndpoint ?? string.Empty,
                snapshot.PrimaryFlaggedServiceName ?? string.Empty
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).Substring(0, 24);
        }

        private static string ExtractLikelyDriverDeviceName(SystemSnapshot snapshot)
        {
            string[] candidates = { snapshot.InvestigationConclusion ?? string.Empty, snapshot.InvestigationSummary ?? string.Empty, snapshot.GuidanceEvidence ?? string.Empty };
            foreach (string value in candidates)
            {
                Match match = Regex.Match(value, @"(?i)(Intel\(R\) Management Engine Interface|Intel Management Engine Interface|Management Engine Interface)");
                if (match.Success) return match.Value;
            }
            return "Management Engine Interface";
        }

        private static IReadOnlyList<string> BuildEvidenceTerms(string question, SystemSnapshot snapshot)
        {
            string combined = string.Join(' ', new[] { question, snapshot.InvestigationReasonCode ?? string.Empty, snapshot.InvestigationConclusion ?? string.Empty, snapshot.InvestigationSummary ?? string.Empty, snapshot.GuidanceTitle ?? string.Empty, snapshot.GuidanceEvidence ?? string.Empty });
            HashSet<string> stop = new(StringComparer.OrdinalIgnoreCase) { "sentinel","windows","computer","current","verified","evidence","issue","problem","found","what","when","where","which","with","from","that","this","have","does","could","would","about","your","there","their","them","then","than","into","still","need","needs","attention" };
            return Regex.Matches(combined.ToLowerInvariant(), @"[a-z0-9][a-z0-9._-]{2,}")
                .Select(m => m.Value).Where(x => !stop.Contains(x) && !int.TryParse(x, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length).Take(18).ToArray();
        }

        private static string NormalizeWebText(string html)
        {
            string withoutScripts = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string withoutTags = Regex.Replace(withoutScripts, @"<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string Classify(string question, SystemSnapshot snapshot)
        {
            string value = (question + " " + snapshot.InvestigationReasonCode + " " + snapshot.InvestigationConclusion + " " + snapshot.InvestigationSummary).ToLowerInvariant();
            if (ContainsAny(value, "driver", "device manager", "hardware id", "management engine", "code 10")) return "driver";
            if (ContainsAny(value, "network", "internet", "dns", "wifi", "ethernet", "connection")) return "network";
            if (ContainsAny(value, "defender", "malware", "virus", "spyware", "threat", "security")) return "security";
            if (ContainsAny(value, "firewall", "port", "blocked connection")) return "firewall";
            if (ContainsAny(value, "windows update", "update error", "kb")) return "windows-update";
            if (ContainsAny(value, "process", "service", "event log", "error code", "exception")) return "windows-diagnostics";
            return "windows-general";
        }

        private static IReadOnlyList<TrustedSource> SourcesFor(string topic) => topic switch
        {
            "driver" => new[] { new TrustedSource("Microsoft Update Catalog", "https://www.catalog.update.microsoft.com/", 95), new TrustedSource("Microsoft Learn - Windows drivers", "https://learn.microsoft.com/windows-hardware/drivers/", 95) },
            "network" => new[] { new TrustedSource("Microsoft Learn - Windows networking", "https://learn.microsoft.com/windows-server/networking/", 95), new TrustedSource("Microsoft Support", "https://support.microsoft.com/windows", 90) },
            "security" => new[] { new TrustedSource("Microsoft Security Intelligence", "https://www.microsoft.com/wdsi", 98), new TrustedSource("Microsoft Learn - Defender", "https://learn.microsoft.com/defender-endpoint/", 95) },
            "firewall" => new[] { new TrustedSource("Microsoft Learn - Windows Firewall", "https://learn.microsoft.com/windows/security/operating-system-security/network-security/windows-firewall/", 95) },
            "windows-update" => new[] { new TrustedSource("Windows release health", "https://learn.microsoft.com/windows/release-health/", 98), new TrustedSource("Microsoft Update Catalog", "https://www.catalog.update.microsoft.com/", 95) },
            "windows-diagnostics" => new[] { new TrustedSource("Microsoft Learn", "https://learn.microsoft.com/windows/", 95), new TrustedSource("Microsoft Support", "https://support.microsoft.com/windows", 90) },
            _ => new[] { new TrustedSource("Microsoft Learn - Windows", "https://learn.microsoft.com/windows/", 95), new TrustedSource("Microsoft Support", "https://support.microsoft.com/windows", 90) }
        };

        private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        private static string Normalize(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        private sealed record TrustedSource(string Name, string Uri, int Authority);
    }

    public sealed record ExternalSourceEvidence(
        string SourceName,
        string Uri,
        int Authority,
        bool Reached,
        bool MatchedCurrentEvidence,
        IReadOnlyList<string> MatchedTerms,
        IReadOnlyList<ExternalResearchPassage>? Passages = null);

    public sealed record ExternalInvestigationResult(
        string Topic,
        bool Verified,
        int ConfidencePercent,
        string Summary,
        IReadOnlyList<ExternalSourceEvidence> Sources,
        bool RequiresAiEscalation,
        bool FromCache,
        IReadOnlyList<string> MatchedTerms,
        bool RequiresSubscription = false,
        bool AiAdvisoryUsed = false,
        bool AiRequiresMoreEvidence = false)
    {
        public static ExternalInvestigationResult NotVerified(string topic, string summary) => new(topic, false, 0, summary, Array.Empty<ExternalSourceEvidence>(), false, false, Array.Empty<string>());
        public static ExternalInvestigationResult SubscriptionRequired(string topic, string summary) => new(topic, false, 0, summary, Array.Empty<ExternalSourceEvidence>(), false, false, Array.Empty<string>(), true);
    }
}
