/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides the Microsoft Learn research boundary used by external investigation.
    /// The desktop app intentionally avoids loading the MCP client SDK because its current
    /// dependency graph requires .NET 10-era runtime assemblies that conflict with Sentinel's
    /// self-contained .NET 8 MSIX. ExternalInvestigationGateway already falls back to bounded,
    /// pinned Microsoft sources when this boundary reports unavailable.
    /// </summary>
    public sealed class MicrosoftLearnResearchClient
    {
        private const int MaximumResults = 5;
        private const int MaximumPassageCharacters = 2_000;

        public Task<MicrosoftLearnResearchResult> SearchAsync(
            string question,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(MicrosoftLearnResearchResult.Unavailable(
                "Microsoft Learn MCP search is disabled in the Windows desktop runtime because the current MCP client dependency requires a newer runtime than Sentinel's supported .NET 8 package. Approved Microsoft source fallback remains enabled."));
        }

        internal static string? FindQueryParameter(JsonElement inputSchema)
        {
            if (inputSchema.ValueKind != JsonValueKind.Object ||
                !inputSchema.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
                return null;

            foreach (string preferred in new[] { "query", "question", "search", "text" })
            {
                if (properties.TryGetProperty(preferred, out JsonElement property) && IsStringProperty(property))
                    return preferred;
            }

            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (IsStringProperty(property.Value)) return property.Name;
            }

            return null;
        }

        internal static IReadOnlyList<MicrosoftLearnDocument> ParseSearchPayload(string payload)
        {
            List<MicrosoftLearnDocument> documents = new();
            TryExtractDocumentsFromText(payload, documents);
            return documents.Where(IsTrustedDocument).Take(MaximumResults).ToArray();
        }

        private static void TryExtractDocumentsFromText(string? text, ICollection<MicrosoftLearnDocument> documents)
        {
            if (string.IsNullOrWhiteSpace(text) || documents.Count >= MaximumResults) return;

            try
            {
                using JsonDocument json = JsonDocument.Parse(text);
                ExtractDocuments(json.RootElement, documents);
            }
            catch (JsonException)
            {
                // Preserve fail-closed parsing: arbitrary prose is never promoted to trusted
                // external evidence when provenance cannot be verified.
            }
        }

        private static void ExtractDocuments(JsonElement element, ICollection<MicrosoftLearnDocument> documents)
        {
            if (documents.Count >= MaximumResults) return;

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ExtractDocuments(item, documents);
                    if (documents.Count >= MaximumResults) break;
                }
                return;
            }

            if (element.ValueKind != JsonValueKind.Object) return;

            string title = ReadString(element, "title");
            string content = ReadString(element, "content");
            string contentUrl = ReadString(element, "contentUrl");
            if (string.IsNullOrWhiteSpace(contentUrl)) contentUrl = ReadString(element, "url");

            if (!string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(contentUrl))
            {
                documents.Add(new MicrosoftLearnDocument(
                    Limit(string.IsNullOrWhiteSpace(title) ? "Microsoft Learn" : title, 240),
                    Limit(content, MaximumPassageCharacters),
                    Limit(contentUrl, 1_000)));
                if (documents.Count >= MaximumResults) return;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                {
                    ExtractDocuments(property.Value, documents);
                    if (documents.Count >= MaximumResults) break;
                }
            }
        }

        private static bool IsStringProperty(JsonElement property)
        {
            if (property.ValueKind != JsonValueKind.Object) return false;
            if (!property.TryGetProperty("type", out JsonElement type)) return true;
            return type.ValueKind == JsonValueKind.String &&
                   type.GetString()?.Equals("string", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static string ReadString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()?.Trim() ?? string.Empty
                : string.Empty;

        private static bool IsTrustedDocument(MicrosoftLearnDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.Content) ||
                !Uri.TryCreate(document.ContentUrl, UriKind.Absolute, out Uri? uri))
                return false;

            return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                   (uri.Host.Equals("learn.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(".learn.microsoft.com", StringComparison.OrdinalIgnoreCase));
        }

        private static string Limit(string value, int maximum) =>
            value.Length <= maximum ? value : value[..maximum];
    }

    public sealed record MicrosoftLearnDocument(string Title, string Content, string ContentUrl);

    public sealed record MicrosoftLearnResearchResult(
        bool Available,
        IReadOnlyList<MicrosoftLearnDocument> Documents,
        string Reason)
    {
        public static MicrosoftLearnResearchResult Unavailable(string reason) =>
            new(false, Array.Empty<MicrosoftLearnDocument>(), reason);
    }
}
