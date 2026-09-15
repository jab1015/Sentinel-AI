using System.Runtime.CompilerServices;
using Sentinel.App.Services;

internal static class QuarantineRecordPathPolicyAcceptance
{
    [ModuleInitializer]
    internal static void Run()
    {
        string valid = Path.Combine(Path.GetTempPath(), "SentinelAI", "sample.bin");
        if (!QuarantineRecordPathPolicy.TryCanonicalize(valid, out string canonical) ||
            !Path.IsPathFullyQualified(canonical))
            throw new InvalidOperationException("Valid quarantine metadata path was not canonicalized.");

        string?[] malformed =
        {
            null,
            string.Empty,
            "   ",
            "bad\0path"
        };

        foreach (string? candidate in malformed)
        {
            bool accepted;
            try
            {
                accepted = QuarantineRecordPathPolicy.TryCanonicalize(candidate, out _);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Malformed quarantine metadata path escaped fail-closed validation ({ex.GetType().Name}).", ex);
            }

            if (accepted)
                throw new InvalidOperationException("Malformed quarantine metadata path was accepted.");
        }

        Console.WriteLine("Malformed quarantine metadata paths fail closed: PASS");
    }
}
