using System.Collections.Generic;

internal static class AiProviderRequestFactory
{
    internal const int MaximumWebSearchToolCalls = 2;

    internal static bool ShouldEnableWebSearch(bool advanced, string? purpose) =>
        advanced && string.Equals(
            purpose?.Trim(),
            "external-investigation",
            StringComparison.OrdinalIgnoreCase);

    internal static Dictionary<string, object?> Create(
        string model,
        int maxOutputTokens,
        string reasoningEffort,
        string evidence,
        bool advanced,
        string purpose)
    {
        bool webSearch = ShouldEnableWebSearch(advanced, purpose);
        string systemPrompt = webSearch
            ? "You are the advisory reasoning layer for Sentinel AI, a Windows monitoring and repair application. Answer the user's actual question directly. For claims about this specific computer, use only the supplied verified machine evidence and never invent missing local facts. For stable general Windows or computer concepts, troubleshooting explanations, and how-to guidance that do not depend on unknown machine state, you may use reliable general knowledge and clearly distinguish that general guidance from facts verified on this computer. For current, latest, vendor-specific, version-specific, or security-advisory claims, use the supplied authoritative research and, when needed, the enabled web search tool. Prefer first-party Microsoft or hardware/software vendor documentation over secondary summaries. Cite claims derived from web search. Web information is external guidance only: it is never proof of this computer's local state, proof that a threat occurred here, or proof that Sentinel, Defender, or another component performed an action. Clearly separate facts from inference. Never claim a repair succeeded, never authorize a system change, and never invent evidence. If a machine-specific answer truly needs more local evidence, say what evidence is needed. Keep the answer useful, concise, and understandable to a nontechnical user."
            : "You are the advisory reasoning layer for Sentinel AI, a Windows monitoring and repair application. Answer the user's actual question directly. For claims about this specific computer, use only the supplied verified machine evidence and never invent missing local facts. For stable general Windows or computer concepts, troubleshooting explanations, and how-to guidance that do not depend on unknown machine state, you may use reliable general knowledge and clearly distinguish that general guidance from facts verified on this computer. Current, latest, vendor-specific, version-specific, or security-advisory claims require supplied authoritative research; do not pretend your model knowledge is current. Clearly separate facts from inference. Never claim a repair succeeded, never authorize a system change, and never invent evidence. If a machine-specific answer truly needs more evidence, say what evidence is needed. Keep the answer useful, concise, and understandable to a nontechnical user.";

        var request = new Dictionary<string, object?>
        {
            ["model"] = model,
            // Sentinel already performs local redaction. Explicitly disable provider-side
            // response retention as an additional privacy boundary for PC/security context.
            ["store"] = false,
            ["max_output_tokens"] = maxOutputTokens,
            ["reasoning"] = new Dictionary<string, object?>
            {
                ["effort"] = reasoningEffort
            },
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "input_text",
                            ["text"] = systemPrompt
                        }
                    }
                },
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "input_text",
                            ["text"] = evidence
                        }
                    }
                }
            }
        };

        if (webSearch)
        {
            request["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "web_search",
                    ["search_context_size"] = "low"
                }
            };
            request["tool_choice"] = "auto";
            request["max_tool_calls"] = MaximumWebSearchToolCalls;
            request["include"] = new[] { "web_search_call.action.sources" };
        }

        return request;
    }
}
