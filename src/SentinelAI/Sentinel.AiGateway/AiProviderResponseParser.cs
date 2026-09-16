using System.Text;
using System.Text.Json;

internal static class AiProviderResponseParser
{
    private const int MaximumCitations = 12;
    private const int MaximumSources = 12;
    private const int MaximumTitleLength = 300;
    private const int MaximumUrlLength = 2_048;

    internal static AiProviderParsedResponse Parse(JsonElement root)
    {
        StringBuilder answer = new();
        List<AiProviderCitation> citations = new();
        List<AiProviderSource> sources = new();
        bool usedWebSearch = false;

        if (root.TryGetProperty("output", out JsonElement output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in output.EnumerateArray())
            {
                string type = ReadString(item, "type");
                if (type.Equals("web_search_call", StringComparison.OrdinalIgnoreCase))
                {
                    usedWebSearch = true;
                    AddToolSources(item, sources);
                    continue;
                }

                if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement part in content.EnumerateArray())
                {
                    if (!ReadString(part, "type").Equals("output_text", StringComparison.OrdinalIgnoreCase) ||
                        !part.TryGetProperty("text", out JsonElement textElement) ||
                        textElement.ValueKind != JsonValueKind.String)
                        continue;

                    string text = textElement.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    int baseOffset = answer.Length;
                    if (answer.Length > 0)
                    {
                        answer.AppendLine();
                        baseOffset = answer.Length;
                    }
                    answer.Append(text);

                    if (part.TryGetProperty("annotations", out JsonElement annotations) &&
                        annotations.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement annotation in annotations.EnumerateArray())
                        {
                            if (citations.Count >= MaximumCitations) break;
                            if (TryReadCitation(annotation, text.Length, baseOffset, out AiProviderCitation? citation) &&
                                citation is not null)
                            {
                                citations.Add(citation);
                                AddSource(sources, citation.Title, citation.Url);
                            }
                        }
                    }
                }
            }
        }

        if (answer.Length == 0 &&
            root.TryGetProperty("output_text", out JsonElement direct) &&
            direct.ValueKind == JsonValueKind.String)
        {
            answer.Append(direct.GetString());
        }

        string rawAnswer = answer.ToString();
        int leadingWhitespace = 0;
        while (leadingWhitespace < rawAnswer.Length && char.IsWhiteSpace(rawAnswer[leadingWhitespace]))
            leadingWhitespace++;

        int retainedEnd = rawAnswer.Length;
        while (retainedEnd > leadingWhitespace && char.IsWhiteSpace(rawAnswer[retainedEnd - 1]))
            retainedEnd--;

        string answerText = retainedEnd <= leadingWhitespace
            ? string.Empty
            : rawAnswer[leadingWhitespace..retainedEnd];

        // URL-citation offsets are calculated against the provider's original output text.
        // Normalize whitespace exactly once here and shift retained citation offsets with it.
        // Downstream layers must not trim this answer again or the links can point at the
        // wrong characters.
        citations = citations
            .Where(citation => citation.StartIndex >= leadingWhitespace &&
                               citation.EndIndex > citation.StartIndex &&
                               citation.EndIndex <= retainedEnd)
            .Select(citation => citation with
            {
                StartIndex = citation.StartIndex - leadingWhitespace,
                EndIndex = citation.EndIndex - leadingWhitespace
            })
            .Where(citation => citation.StartIndex >= 0 &&
                               citation.EndIndex > citation.StartIndex &&
                               citation.EndIndex <= answerText.Length)
            .OrderBy(citation => citation.StartIndex)
            .ThenBy(citation => citation.EndIndex)
            .Take(MaximumCitations)
            .ToList();

        return new AiProviderParsedResponse(
            Answer: answerText,
            InputTokens: ReadUsage(root, "input_tokens"),
            OutputTokens: ReadUsage(root, "output_tokens"),
            UsedWebSearch: usedWebSearch,
            Citations: citations,
            Sources: sources.Take(MaximumSources).ToArray());
    }

    private static bool TryReadCitation(
        JsonElement annotation,
        int textLength,
        int baseOffset,
        out AiProviderCitation? citation)
    {
        citation = null;
        JsonElement value = annotation;
        string type = ReadString(annotation, "type");

        if (annotation.TryGetProperty("url_citation", out JsonElement nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            value = nested;
            if (string.IsNullOrWhiteSpace(type)) type = "url_citation";
        }

        if (!type.Equals("url_citation", StringComparison.OrdinalIgnoreCase)) return false;
        if (!TryReadHttpsUrl(value, "url", out string url)) return false;
        if (!TryReadInt(value, "start_index", out int start) ||
            !TryReadInt(value, "end_index", out int end) ||
            start < 0 || end <= start || end > textLength)
            return false;

        citation = new AiProviderCitation(
            StartIndex: checked(baseOffset + start),
            EndIndex: checked(baseOffset + end),
            Url: url,
            Title: NormalizeTitle(ReadString(value, "title"), url));
        return true;
    }

    private static void AddToolSources(JsonElement item, List<AiProviderSource> sources)
    {
        if (!item.TryGetProperty("action", out JsonElement action) || action.ValueKind != JsonValueKind.Object ||
            !action.TryGetProperty("sources", out JsonElement sourceArray) || sourceArray.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement source in sourceArray.EnumerateArray())
        {
            if (sources.Count >= MaximumSources) break;
            if (!TryReadHttpsUrl(source, "url", out string url)) continue;
            AddSource(sources, ReadString(source, "title"), url);
        }
    }

    private static void AddSource(List<AiProviderSource> sources, string? title, string url)
    {
        if (sources.Count >= MaximumSources ||
            sources.Any(source => source.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            return;

        sources.Add(new AiProviderSource(
            Title: NormalizeTitle(title, url),
            Url: url));
    }

    private static bool TryReadHttpsUrl(JsonElement element, string property, out string url)
    {
        url = string.Empty;
        string candidate = ReadString(element, property).Trim();
        if (candidate.Length == 0 || candidate.Length > MaximumUrlLength ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        url = uri.AbsoluteUri;
        return true;
    }

    private static string NormalizeTitle(string? title, string url)
    {
        string value = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value) && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            value = uri.Host;
        if (value.Length > MaximumTitleLength) value = value[..MaximumTitleLength];
        return value;
    }

    private static bool TryReadInt(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out JsonElement item) && item.TryGetInt32(out value);
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static int ReadUsage(JsonElement root, string property)
    {
        if (root.TryGetProperty("usage", out JsonElement usage) &&
            usage.TryGetProperty(property, out JsonElement value) &&
            value.TryGetInt32(out int number))
            return Math.Max(0, number);
        return 0;
    }
}

internal sealed record AiProviderParsedResponse(
    string Answer,
    int InputTokens,
    int OutputTokens,
    bool UsedWebSearch,
    IReadOnlyList<AiProviderCitation> Citations,
    IReadOnlyList<AiProviderSource> Sources);

public sealed record AiProviderCitation(int StartIndex, int EndIndex, string Url, string Title);
public sealed record AiProviderSource(string Title, string Url);
