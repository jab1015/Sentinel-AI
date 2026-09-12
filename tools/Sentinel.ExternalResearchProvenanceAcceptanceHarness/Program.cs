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

Console.WriteLine("External research provenance acceptance harness PASS");
