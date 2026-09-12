/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Extracts small, bounded passages around evidence-term matches. A passage is
    /// attributable context only; it is never itself proof of local machine state or
    /// proof that Sentinel performed a security action.
    /// </summary>
    public static class ExternalResearchProvenancePolicy
    {
        public const int MaximumPassagesPerSource = 6;
        public const int MaximumPassageCharacters = 360;

        public static IReadOnlyList<ExternalResearchPassage> ExtractPassages(
            string? normalizedSourceText,
            IEnumerable<string>? evidenceTerms)
        {
            string text = Normalize(normalizedSourceText);
            if (text.Length == 0 || evidenceTerms is null)
                return Array.Empty<ExternalResearchPassage>();

            List<ExternalResearchPassage> passages = new();
            HashSet<string> seenTerms = new(StringComparer.OrdinalIgnoreCase);

            foreach (string rawTerm in evidenceTerms)
            {
                if (passages.Count >= MaximumPassagesPerSource)
                    break;

                string term = Normalize(rawTerm);
                if (term.Length < 3 || !seenTerms.Add(term))
                    continue;

                int index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;

                string passage = ExtractWindow(text, index, term.Length);
                if (passage.Length == 0)
                    continue;

                // Distinct evidence terms are preserved even when their bounded windows
                // are textually identical. Otherwise one nearby term can erase another
                // term's source attribution and make passage-to-claim review ambiguous.
                passages.Add(new ExternalResearchPassage(term, passage));
            }

            return passages;
        }

        public static bool HasAttributableMatch(
            IReadOnlyList<ExternalResearchPassage>? passages,
            string? term)
        {
            if (passages is null || string.IsNullOrWhiteSpace(term))
                return false;

            return passages.Any(p =>
                p.MatchedTerm.Equals(term.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.Passage));
        }

        private static string ExtractWindow(string text, int matchIndex, int termLength)
        {
            int halfWindow = MaximumPassageCharacters / 2;
            int start = Math.Max(0, matchIndex - halfWindow);
            int end = Math.Min(text.Length, matchIndex + termLength + halfWindow);

            if (start > 0)
            {
                int nextSpace = text.IndexOf(' ', start);
                if (nextSpace >= start && nextSpace < matchIndex)
                    start = nextSpace + 1;
            }

            if (end < text.Length)
            {
                int priorSpace = text.LastIndexOf(' ', end - 1, Math.Max(0, end - matchIndex));
                if (priorSpace > matchIndex)
                    end = priorSpace;
            }

            if (end <= start)
                return string.Empty;

            string passage = text[start..end].Trim();
            if (passage.Length > MaximumPassageCharacters)
                passage = passage[..MaximumPassageCharacters].TrimEnd();
            return passage;
        }

        private static string Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(value, @"\s+", " ").Trim();
    }

    public sealed record ExternalResearchPassage(string MatchedTerm, string Passage);
}
