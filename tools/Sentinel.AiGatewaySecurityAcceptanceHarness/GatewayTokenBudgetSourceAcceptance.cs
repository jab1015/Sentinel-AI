using System.Runtime.CompilerServices;

internal static class GatewayTokenBudgetSourceAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string? sourcePath = FindGatewayProgram();
        if (sourcePath is null)
            throw new InvalidOperationException("Could not locate Sentinel.AiGateway/Program.cs for token-budget source acceptance.");

        string source = File.ReadAllText(sourcePath);
        Require(source, "const int MinimumTotalTokenBudget = 192;", "minimum total-token budget validation");
        Require(source, "request.MaximumTotalTokens > tierMaximum", "tier maximum total-token budget validation");
        Require(source, "int requestedBudget = request.MaximumTotalTokens;", "authorized requested-budget preservation");
        Require(source, "long totalTokens = (long)inputTokens + outputTokens;", "provider total-token accounting");
        Require(source, "totalTokens > requestedBudget", "post-response total-token budget rejection");
        Require(source, "OPENAI_GATEWAY_TOKEN_BUDGET_VIOLATION", "budget-violation audit marker");
    }

    private static string? FindGatewayProgram()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "src", "SentinelAI", "Sentinel.AiGateway", "Program.cs");
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static void Require(string source, string marker, string description)
    {
        if (!source.Contains(marker, StringComparison.Ordinal))
            throw new InvalidOperationException($"Gateway token-budget acceptance failed: missing {description} marker '{marker}'.");
    }
}
