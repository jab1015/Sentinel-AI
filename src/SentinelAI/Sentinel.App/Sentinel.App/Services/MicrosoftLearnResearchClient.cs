/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Performs bounded, read-only research against Microsoft's public Learn MCP server.
    /// Tool discovery is performed for every connection so Sentinel does not depend on a
    /// permanently hard-coded MCP request schema.
    /// </summary>
    public sealed class MicrosoftLearnResearchClient
    {
        private static readonly Uri Endpoint = new("https://learn.microsoft.com/api/mcp");
        private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(15);
        private const int MaximumResults = 5;
        private const int MaximumPassageCharacters = 2_000;

        public async Task<MicrosoftLearnResearchResult> SearchAsync(
            string question,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(OverallTimeout);

            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = Endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = TimeSpan.FromSeconds(8),
                    EnableStandaloneGetStream = false
                });

                await using McpClient client = await McpClient.CreateAsync(
                    transport,
                    cancellationToken: deadline.Token).ConfigureAwait(false);

                IList<McpClientTool> tools = await client.ListToolsAsync(
                    cancellationToken: deadline.Token).ConfigureAwait(false);
                McpClientTool? searchTool = tools.FirstOrDefault(tool =>
                    tool.Name.Equals("microsoft_docs_search", StringComparison.OrdinalIgnoreCase));
                if (searchTool is null)
                    return MicrosoftLearnResearchResult.Unavailable("Microsoft Learn did not advertise its documentation search tool.");

                string? queryParameter = FindQueryParameter(searchTool.ProtocolTool.InputSchema);
                if (string.IsNullOrWhiteSpace(queryParameter))
                    return MicrosoftLearnResearchResult.Unavailable("Microsoft Learn's search tool did not advertise a compatible text query parameter.");

                CallToolResult result = await searchTool.CallAsync(
                    new Dictionary<string, object?> { [queryParameter] = Limit(question.Trim(), 500) },
                    cancellationToken: deadline.Token).ConfigureAwait(false);

                if (result.IsError is true)
                    return MicrosoftLearnResearchResult.Unavailable("Microsoft Learn returned an error for the documentation search.");

                List<MicrosoftLearnDocument> documents = new();
                if (result.StructuredContent is JsonElement structured)
                    ExtractDocuments(structured, documents);

                foreach (TextContentBlock textBlock in result.Content.OfType<TextContentBlock>())
                {
                    if (documents.Count >= MaximumResults) break;
                    TryExtractDocumentsFromText(textBlock.Text, documents);
                }

                MicrosoftLearnDocument[] bounded = documents
                    .Where(IsTrustedDocument)
                    .GroupBy(item => item.ContentUrl, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(MaximumResults)
                    .ToArray();

                return new MicrosoftLearnResearchResult(
                    Available: true,
                    Documents: bounded,
                    Reason: bounded.Length > 0
                        ? $"Microsoft Learn returned {bounded.Length} authoritative documentation result(s)."
                        : "Microsoft Learn search completed but returned no usable authoritative documentation results.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return MicrosoftLearnResearchResult.Unavailable("Microsoft Learn documentation search timed out.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return MicrosoftLearnResearchResult.Unavailable("Microsoft Learn documentation search was temporarily unavailable.");
            }
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
                // MCP tool output is expected to be structured JSON. Do not convert arbitrary
                // prose into a trusted external document if provenance cannot be preserved.
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
