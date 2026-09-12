using Sentinel.App.Services;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string text = "Microsoft Defender Antivirus real-time protection helps monitor files and processes. " +
              "Windows Firewall applies rules to network traffic. " +
              new string('x', 1000) +
              " A driver package must match the hardware identifier before installation.";

var passages = ExternalResearchProvenancePolicy.ExtractPassages(
    text,
    new[] { "real-time protection", "Windows Firewall", "hardware identifier", "missing term" });

Assert(passages.Count == 3, "Expected exactly three attributable passages.");
Assert(passages.All(p => p.Passage.Length <= ExternalResearchProvenancePolicy.MaximumPassageCharacters), "Passage exceeded maximum length.");
Assert(ExternalResearchProvenancePolicy.HasAttributableMatch(passages, "Windows Firewall"), "Expected firewall passage binding.");
Assert(!ExternalResearchProvenancePolicy.HasAttributableMatch(passages, "missing term"), "Missing term must not be attributable.");
Assert(passages.All(p => p.Passage.Contains(p.MatchedTerm, StringComparison.OrdinalIgnoreCase)), "Each passage must contain its bound term.");

var duplicates = ExternalResearchProvenancePolicy.ExtractPassages("alpha alpha alpha", new[] { "alpha", "ALPHA", " alpha " });
Assert(duplicates.Count == 1, "Duplicate terms should not produce duplicate provenance entries.");

var empty = ExternalResearchProvenancePolicy.ExtractPassages(null, new[] { "alpha" });
Assert(empty.Count == 0, "Null source text must produce no passage evidence.");

var capTerms = Enumerable.Range(0, 20).Select(i => $"term{i}").ToArray();
string capText = string.Join(' ', capTerms);
var capped = ExternalResearchProvenancePolicy.ExtractPassages(capText, capTerms);
Assert(capped.Count == ExternalResearchProvenancePolicy.MaximumPassagesPerSource, "Passage count must be capped.");

Assert(
    ExternalResearchProvenancePolicy.TryResolveDellPackageUri("driver/network/example.exe", out string relativeDell) &&
    relativeDell.StartsWith("https://downloads.dell.com/", StringComparison.OrdinalIgnoreCase),
    "Relative Dell catalog paths must resolve only under the Dell HTTPS download authority.");
Assert(
    ExternalResearchProvenancePolicy.TryResolveDellPackageUri("https://downloads.dell.com/FOLDER/driver.exe", out string absoluteDell) &&
    absoluteDell.StartsWith("https://downloads.dell.com/", StringComparison.OrdinalIgnoreCase),
    "Explicit Dell HTTPS package URL should be accepted.");
Assert(
    !ExternalResearchProvenancePolicy.TryResolveDellPackageUri("http://downloads.dell.com/FOLDER/driver.exe", out _),
    "Plain HTTP Dell package URL must be rejected.");
Assert(
    !ExternalResearchProvenancePolicy.TryResolveDellPackageUri("https://evil.example/FOLDER/driver.exe", out _),
    "Non-Dell package authority must be rejected.");
Assert(
    !ExternalResearchProvenancePolicy.TryResolveDellPackageUri("https://downloads.dell.com:444/FOLDER/driver.exe", out _),
    "Alternate Dell port must be rejected.");
Assert(
    !ExternalResearchProvenancePolicy.TryResolveDellPackageUri("https://user@downloads.dell.com/FOLDER/driver.exe", out _),
    "Credential-bearing package URI must be rejected.");
Assert(
    !ExternalResearchProvenancePolicy.TryResolveDellPackageUri("https://downloads.dell.com/FOLDER/readme.txt", out _),
    "Non-executable catalog package path must be rejected.");

Console.WriteLine("External research provenance acceptance harness PASS");
