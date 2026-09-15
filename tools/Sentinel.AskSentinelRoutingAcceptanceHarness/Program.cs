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

AskSentinelRoute defenderStatus = routing.Decide("Is Defender on?", localAnswerInsufficient: false);
Require(!defenderStatus.UseBasicAi && !defenderStatus.UseExternalResearch,
    "Clear Defender state question unnecessarily escalated beyond local evidence.");

AskSentinelRoute ambiguousApp = routing.Decide("Which app is best for editing photos?", localAnswerInsufficient: false);
Require(ambiguousApp.UseBasicAi && !ambiguousApp.UseExternalResearch,
    "General app question was incorrectly trusted as a local running-process answer.");

AskSentinelRoute ambiguousVirus = routing.Decide("What virus is the worst?", localAnswerInsufficient: false);
Require(ambiguousVirus.UseBasicAi && !ambiguousVirus.UseExternalResearch,
    "General virus question was incorrectly trusted as a local security-status answer.");

AskSentinelRoute latest = routing.Decide("Search the web for the latest Windows 11 known issue", localAnswerInsufficient: true);
Require(!latest.UseBasicAi && latest.UseExternalResearch,
    "Explicit current web-research request did not route directly to external research.");

AskSentinelRoute naturalResearch = routing.Decide("Research why this Windows update is failing", localAnswerInsufficient: true);
Require(!naturalResearch.UseBasicAi && naturalResearch.UseExternalResearch,
    "Natural 'research why' wording did not route directly to external research.");

AskSentinelRoute accordingTo = routing.Decide("According to Microsoft, what does this security advisory mean?", localAnswerInsufficient: true);
Require(!accordingTo.UseBasicAi && accordingTo.UseExternalResearch,
    "'According to Microsoft' wording did not route directly to authoritative research.");

var escalation = new AiEscalationPolicy();
AiEscalationDecision firstPass = escalation.Evaluate(routing.CreateBasicAiContext(
    "Could ransomware cause a complicated security problem?"));
Require(firstPass.UseCloudAi && firstPass.ModelTier == AiModelTier.Economy,
    "First unresolved question did not stay on the Basic/economy AI tier.");
Require(firstPass.MaximumTotalTokens == AiEscalationPolicy.BasicMaximumTotalTokens &&
        firstPass.MaximumTotalTokens <= 900,
    "Basic AI request budget exceeds the production gateway Basic tier limit.");

AiEscalationDecision researched = escalation.Evaluate(routing.CreateExternalAiContext(
    "Analyze this high-risk ransomware issue using the current sources",
    "security",
    sourceCount: 3,
    authoritativeConclusionVerified: false));
Require(researched.UseCloudAi && researched.ModelTier == AiModelTier.Advanced,
    "Complex high-risk question did not become eligible for Advanced AI after external research.");
Require(researched.MaximumTotalTokens == AiEscalationPolicy.AdvancedMaximumTotalTokens &&
        researched.MaximumTotalTokens <= 2500,
    "Advanced AI request budget exceeds the production gateway Advanced tier limit.");

Console.WriteLine("Unknown phrasing -> Basic AI: PASS");
Console.WriteLine("Verified broad local overview -> local answer: PASS");
Console.WriteLine("Definition/explanation -> Basic AI: PASS");
Console.WriteLine("Direct device status -> local answer: PASS");
Console.WriteLine("Ambiguous app/security keywords -> Basic AI: PASS");
Console.WriteLine("Fresh/current/research/according-to wording -> external research: PASS");
Console.WriteLine("Basic-first / Advanced-after-research tiering: PASS");
Console.WriteLine("Basic/Advanced token budgets match gateway limits: PASS");
Console.WriteLine("RESULT: PASS");
