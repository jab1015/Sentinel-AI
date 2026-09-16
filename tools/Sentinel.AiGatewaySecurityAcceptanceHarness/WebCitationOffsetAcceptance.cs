using System.Runtime.CompilerServices;
using System.Text.Json;

internal static class WebCitationOffsetAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        const string response = """
        {
          "output": [
            {
              "type": "web_search_call",
              "action": {
                "sources": [
                  { "url": "https://www.nvidia.com/", "title": "NVIDIA" }
                ]
              }
            },
            {
              "type": "message",
              "content": [
                {
                  "type": "output_text",
                  "text": "  NVIDIA guidance.  ",
                  "annotations": [
                    {
                      "type": "url_citation",
                      "start_index": 2,
                      "end_index": 8,
                      "url": "https://www.nvidia.com/",
                      "title": "NVIDIA"
                    }
                  ]
                }
              ]
            }
          ],
          "usage": { "input_tokens": 20, "output_tokens": 8 }
        }
        """;

        using JsonDocument document = JsonDocument.Parse(response);
        AiProviderParsedResponse parsed = AiProviderResponseParser.Parse(document.RootElement);

        if (!parsed.Answer.Equals("NVIDIA guidance.", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Citation-offset acceptance failed: normalized answer was '{parsed.Answer}'.");

        if (parsed.Citations.Count != 1 ||
            parsed.Citations[0].StartIndex != 0 ||
            parsed.Citations[0].EndIndex != 6 ||
            !parsed.Answer[parsed.Citations[0].StartIndex..parsed.Citations[0].EndIndex]
                .Equals("NVIDIA", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Citation-offset acceptance failed: whitespace normalization did not preserve the cited text range.");
        }
    }
}
