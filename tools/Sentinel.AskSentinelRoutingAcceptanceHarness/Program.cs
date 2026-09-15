using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI Adaptive Ask Routing Acceptance ===");

var routing = new AskSentinelRoutingPolicy();

AskSentinelRoute unknown = routing.Decide("Could you help me understand this strange thing I noticed?", localAnswerInsufficient: true);
Require(unknown.UseBasicAi, "Unrecognized natural-language question did not route to Basic AI.");
Require(!unknown.UseExternalResearch, "Ordinary unresolved question incorrectly required external research first.");

AskSentinelRoute broadPc = routing.Decide("Tell me what's going on with my computer", localAnswerInsufficient: false);
Require(!broadPc.UseBasicAi && !broadPc.UseExternalResearch,
    "A verified broad local PC overview was not allowed to answer directly.");

AskSentinelRoute definition = routing.Decide("What is TPM?", localAnswerInsufficient: false);
Require(definition.UseBasicAi && definition.ExplanationRequested,
    "Definition question with a terse local handler did not receive Basic AI explanation.");

AskSentinelRoute explanation = routing.Decide("How does BitLocker protect me?", localAnswerInsufficient: false);
Require(explanation.UseBasicAi && explanation.ExplanationRequested,
    "Explanatory Windows question did not receive Basic AI help.");

AskSentinelRoute directStatus = routing.Decide("What is my firewall status?", localAnswerInsufficient: false);
Require(!directStatus.UseBasicAi && !directStatus.UseExternalResearch,
    "Direct local status question unnecessarily escalated beyond verified local evidence.");

AskSentinelRoute latest = routing.Decide("Search the web for the latest Windows 11 known issue", localAnswerInsufficient: true);
Require(!latest.UseBasicAi && latest.UseExternalResearch,
    "Explicit current web-research request did not route directly to external research.");

var escalation = new AiEscalationPolicy();
AiEscalationDecision firstPass = escalation.Evaluate(routing.CreateBasicAiContext(
    "Could ransomware cause a complicated security problem?"));
Require(firstPass.UseCloudAi && firstPass.ModelTier == AiModelTier.Economy,
    "First unresolved question did not stay on the Basic/economy AI tier.");

AiEscalationDecision researched = escalation.Evaluate(routing.CreateExternalAiContext(
    "Analyze this high-risk ransomware issue using the current sources",
    "security",
    sourceCount: 3,
    authoritativeConclusionVerified: false));
Require(researched.UseCloudAi && researched.ModelTier == AiModelTier.Advanced,
    "Complex high-risk question did not become eligible for Advanced AI after external research.");

Console.WriteLine("Unknown phrasing -> Basic AI: PASS");
Console.WriteLine("Verified broad local overview -> local answer: PASS");
Console.WriteLine("Definition/explanation -> Basic AI: PASS");
Console.WriteLine("Direct device status -> local answer: PASS");
Console.WriteLine("Fresh/current request -> external research: PASS");
Console.WriteLine("Basic-first / Advanced-after-research tiering: PASS");
Console.WriteLine("RESULT: PASS");
