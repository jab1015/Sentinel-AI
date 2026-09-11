using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI System Image Health Acceptance ===");

var healthyDism = SystemImageHealthAssessmentService.ClassifyDismText(0, "No component store corruption detected. The operation completed successfully.");
Require(healthyDism == IntegrityAssessmentState.Healthy, $"Healthy DISM text classified as {healthyDism}.");
Console.WriteLine("DISM healthy phrase: PASS");

var corruptDism = SystemImageHealthAssessmentService.ClassifyDismText(0, "The component store is repairable. Component store corruption detected.");
Require(corruptDism == IntegrityAssessmentState.Corrupt, $"Corrupt DISM text classified as {corruptDism}.");
Console.WriteLine("DISM corruption phrase: PASS");

var ambiguousDism = SystemImageHealthAssessmentService.ClassifyDismText(0, "The operation completed successfully.");
Require(ambiguousDism == IntegrityAssessmentState.Unknown, $"Ambiguous DISM text classified as {ambiguousDism} instead of Unknown.");
Console.WriteLine("DISM ambiguous output fails closed: PASS");

var errorDism = SystemImageHealthAssessmentService.ClassifyDismText(5, "No component store corruption detected.");
Require(errorDism == IntegrityAssessmentState.Error, $"Nonzero DISM result classified as {errorDism}.");
Console.WriteLine("DISM nonzero exit cannot claim healthy: PASS");

var healthySfc = SystemImageHealthAssessmentService.ClassifySfcText(0, "Windows Resource Protection did not find any integrity violations.");
Require(healthySfc == IntegrityAssessmentState.Healthy, $"Healthy SFC text classified as {healthySfc}.");
Console.WriteLine("SFC healthy phrase: PASS");

var corruptSfc = SystemImageHealthAssessmentService.ClassifySfcText(1, "Windows Resource Protection found corrupt files and successfully repaired them.");
Require(corruptSfc == IntegrityAssessmentState.Corrupt, $"Corrupt SFC text classified as {corruptSfc}.");
Console.WriteLine("SFC corruption phrase: PASS");

var ambiguousSfc = SystemImageHealthAssessmentService.ClassifySfcText(0, "Verification completed.");
Require(ambiguousSfc == IntegrityAssessmentState.Unknown, $"Ambiguous SFC text classified as {ambiguousSfc} instead of Unknown.");
Console.WriteLine("SFC ambiguous output fails closed: PASS");

Console.WriteLine("RESULT: PASS");
