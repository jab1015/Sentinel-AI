using Microsoft.UI.Xaml;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private const string AskSentinelOriginalQuestionMarker = "\nOriginal question: ";
        private readonly StartupLogonEvidenceProvider _askSentinelStartupLogonEvidence = new();

        private enum AskSentinelFollowUpAction
        {
            ExplainMore,
            SearchSources,
            NextSteps
        }

        private async void AskSentinelExplainMoreButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelFollowUpAsync(AskSentinelFollowUpAction.ExplainMore);
        }

        private async void AskSentinelSearchSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelFollowUpAsync(AskSentinelFollowUpAction.SearchSources);
        }

        private async void AskSentinelNextStepsButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelFollowUpAsync(AskSentinelFollowUpAction.NextSteps);
        }

        private async Task SubmitAskSentinelFollowUpAsync(AskSentinelFollowUpAction action)
        {
            if (_askSentinelBusy) return;

            string current = AskSentinelQuestionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                AskSentinelStatusText.Text = "Ask Sentinel a question first, then choose a follow-up.";
                return;
            }

            string original = ExtractAskSentinelOriginalQuestion(current);
            string previousDisplayedAnswer = AskSentinelAnswerText.Text?.Trim() ?? string.Empty;

            _askSentinelBusy = true;
            AskSentinelButton.IsEnabled = false;
            AskSentinelQuestionBox.IsEnabled = false;
            ClearAskSentinelResearchSources();
            HideAskSentinelRepairActions();
            AskSentinelProgressPanel.Visibility = Visibility.Visible;
            AskSentinelProgressRing.IsActive = true;
            AskSentinelStatusText.Text = "Continuing from the verified answer…";

            try
            {
                await _engine.RefreshAsync();
                var snapshot = _engine.CurrentSnapshot;
                var history = await _investigationHistoryService.ReadRecentAsync(100);

                AskSentinelProgressText.Text = "Re-checking this computer's evidence…";
                AskSentinelResponseOrchestrator.AskSentinelResponse localResponse = await Task.Run(() =>
                    _askSentinelResponseOrchestrator.CreateResponse(original, snapshot, history));

                string localAnswer = !string.IsNullOrWhiteSpace(localResponse.Answer)
                    ? localResponse.Answer.Trim()
                    : previousDisplayedAnswer;
                if (string.IsNullOrWhiteSpace(localAnswer))
                    localAnswer = "Sentinel does not yet have enough verified local information to restate the earlier finding.";

                IReadOnlyList<CloudAiCitation> displayCitations = Array.Empty<CloudAiCitation>();
                IReadOnlyList<CloudAiSource> displaySources = Array.Empty<CloudAiSource>();
                AskSentinelProvenanceLabel provenance = AskSentinelProvenanceLabel.Observed;
                string answer;
                bool insufficient = localResponse.IsInsufficientEvidence;
                string grounding;

                if (action == AskSentinelFollowUpAction.SearchSources)
                {
                    AskSentinelProgressText.Text = "Keeping the local findings and checking current authoritative sources…";
                    ExternalInvestigationResult external = await _externalInvestigationGateway.InvestigateAsync(original, snapshot);

                    string externalAnswer;
                    if (external.RequiresSubscription)
                    {
                        externalAnswer = external.Summary +
                            "\n\nThe verified local startup findings above remain the active evidence for this computer.";
                    }
                    else
                    {
                        SmartAiResult externalAi = await _askSentinelAiCoordinator.AnalyzeAsync(
                            "external-investigation-follow-up",
                            original,
                            snapshot,
                            external,
                            _askSentinelRoutingPolicy.CreateExternalAiContext(
                                original,
                                external.Topic,
                                external.Sources.Count,
                                external.Verified));

                        externalAnswer = BuildExternalResearchFollowUp(external, externalAi);
                        displayCitations = externalAi.UsedWebSearch
                            ? externalAi.Citations
                            : Array.Empty<CloudAiCitation>();
                        displaySources = MergeAskSentinelResearchSources(external, externalAi.Sources);

                        string sourceNames = external.Sources.Count == 0
                            ? "none"
                            : string.Join(", ", external.Sources.Select(x => x.SourceName).Distinct());
                        string fingerprint = $"external-follow-up:{external.Topic}:{original.Trim().ToLowerInvariant()}";
                        await _investigationHistoryService.RecordAsync(
                            fingerprint,
                            "External follow-up investigation",
                            external.Summary,
                            external.Sources.Count > 0 ? "Information" : "Attention",
                            external.RequiresAiEscalation,
                            false);
                        _askSentinelOutcomeRecorder.RecordInvestigation(
                            "External follow-up investigation",
                            external.Summary,
                            external.RequiresAiEscalation,
                            $"Topic: {external.Topic}; Confidence: {external.ConfidencePercent}%; Sources: {sourceNames}");
                        UpdateMaintenanceReport();
                    }

                    answer =
                        "What I verified locally:\n\n" + localAnswer +
                        "\n\nCurrent authoritative-source research:\n\n" + externalAnswer;
                    insufficient = false;
                    provenance = AskSentinelProvenanceLabel.Advisory;
                    grounding = "Sentinel preserved the freshly re-checked local answer and added current authoritative-source research without treating external guidance as proof of this computer's state.";
                }
                else if (action == AskSentinelFollowUpAction.NextSteps && IsStartupLogonQuestion(original))
                {
                    AskSentinelProgressText.Text = "Running a deeper local startup and sign-in investigation…";
                    answer = await Task.Run(() => _askSentinelStartupLogonEvidence.GetDeepStartupLogonEvidence(snapshot));
                    insufficient = false;
                    provenance = AskSentinelProvenanceLabel.Observed;
                    grounding = "Sentinel performed the offered deeper startup/sign-in investigation using bounded local Windows event, startup, task, service, and process evidence only.";
                }
                else
                {
                    bool nextSteps = action == AskSentinelFollowUpAction.NextSteps;
                    AskSentinelProgressText.Text = nextSteps
                        ? "Deriving safe next steps from the local evidence…"
                        : "Explaining the local evidence in more detail…";

                    string followUpPrompt = nextSteps
                        ? "Using only the current verified local evidence, explain the safest next steps for the original question. Prefer actions Sentinel can perform or verify itself before telling the user to do manual work. Do not claim a cause that the evidence does not prove. Do not claim any action was performed. " +
                          $"Original question: {LimitFollowUpQuestion(original)} Current verified local answer: {LimitFollowUpAnswer(localAnswer)}"
                        : "Explain the current verified local answer in clearer, more detailed language. Separate measured facts, likely contributors, and anything that is still unknown. If Sentinel can perform a safe additional diagnostic itself, say so explicitly. Do not replace local evidence with generic web guidance. " +
                          $"Original question: {LimitFollowUpQuestion(original)} Current verified local answer: {LimitFollowUpAnswer(localAnswer)}";

                    SmartAiResult followUpAi = await _askSentinelAiCoordinator.AnalyzeAsync(
                        nextSteps ? "ask-sentinel-next-steps" : "ask-sentinel-explain-more",
                        followUpPrompt,
                        snapshot,
                        null,
                        _askSentinelRoutingPolicy.CreateBasicAiContext(followUpPrompt));

                    if (followUpAi.UsedCloudAi && !string.IsNullOrWhiteSpace(followUpAi.Answer))
                    {
                        answer = followUpAi.Answer.Trim();
                        insufficient = followUpAi.RequiresMoreEvidence;
                        provenance = AskSentinelProvenanceLabel.Advisory;
                        grounding = nextSteps
                            ? "Sentinel used AI only to explain safe next steps from the freshly re-checked local evidence."
                            : "Sentinel used AI only to explain the freshly re-checked local evidence in clearer language.";
                    }
                    else
                    {
                        answer = nextSteps
                            ? BuildDeterministicNextSteps(localAnswer)
                            : BuildDeterministicExplainMore(localAnswer);
                        insufficient = false;
                        grounding = nextSteps
                            ? "Sentinel derived conservative next steps directly from the freshly re-checked local answer because AI interpretation was unavailable."
                            : "Sentinel expanded the freshly re-checked local answer without external research because AI interpretation was unavailable.";
                    }
                }

                AskSentinelResponseOrchestrator.AskSentinelResponse composed = localResponse with
                {
                    Answer = answer,
                    IsInsufficientEvidence = insufficient,
                    UsedInvestigationHistory = false,
                    UsedRecommendationGuard = false,
                    PassedFinalSafetyValidation = false,
                    GroundingSummary = grounding
                };

                AskSentinelResponseSafetyValidator.ValidationResult validation =
                    _askSentinelResponseSafetyValidator.ValidateForDisplay(composed, snapshot, provenance);

                if (!validation.IsSafe)
                {
                    RenderAskSentinelAnswer(validation.Answer);
                    AskSentinelStatusText.Text = "The follow-up was blocked because it could not be grounded safely.";
                }
                else
                {
                    RenderAskSentinelAnswer(validation.Answer, displayCitations, displaySources);
                    AskSentinelStatusText.Text = action switch
                    {
                        AskSentinelFollowUpAction.SearchSources => "Kept the verified local findings and reported the current authoritative-source search result.",
                        AskSentinelFollowUpAction.NextSteps when IsStartupLogonQuestion(original) => "Completed the deeper local startup and sign-in investigation.",
                        AskSentinelFollowUpAction.NextSteps => "Next steps are based on the current verified local evidence.",
                        _ => "Expanded the answer from the current verified local evidence."
                    };
                }

                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                if (!string.IsNullOrWhiteSpace(previousDisplayedAnswer))
                {
                    RenderAskSentinelAnswer(
                        previousDisplayedAnswer +
                        "\n\nI couldn't complete the follow-up layer safely, so I kept the last validated answer instead of replacing it with an unrelated result.");
                    AskSentinelAnswerBorder.Visibility = Visibility.Visible;
                }
                AskSentinelStatusText.Text = "The follow-up could not be completed safely; the prior verified answer was preserved.";
            }
            finally
            {
                AskSentinelProgressRing.IsActive = false;
                AskSentinelProgressPanel.Visibility = Visibility.Collapsed;
                _askSentinelBusy = false;
                AskSentinelButton.IsEnabled = true;
                AskSentinelQuestionBox.IsEnabled = true;
                AskSentinelQuestionBox.Focus(FocusState.Programmatic);
            }
        }

        private static string BuildExternalResearchFollowUp(ExternalInvestigationResult external, SmartAiResult externalAi)
        {
            if (external.Sources.Count == 0)
            {
                return "Sentinel completed the authoritative-source search but did not find relevant attributable source material for this question. " +
                       "That search result does not override or weaken the verified local findings above.";
            }

            string sources = string.Join(", ", external.Sources.Select(x => x.SourceName).Distinct().Take(6));
            string matched = external.MatchedTerms.Count > 0
                ? " Matched terms included: " + string.Join(", ", external.MatchedTerms.Take(8)) + "."
                : string.Empty;
            string research = $"Sentinel reached {external.Sources.Count} approved source result(s): {sources}.{matched}";

            if (externalAi.UsedCloudAi && !string.IsNullOrWhiteSpace(externalAi.Answer))
                return research + "\n\nInterpretation of the external material:\n" + externalAi.Answer.Trim();

            return research + "\n\n" + external.Summary +
                   "\n\nNo AI interpretation was substituted for missing evidence; the local findings above remain the evidence about this computer.";
        }

        private static string BuildDeterministicExplainMore(string localAnswer) =>
            "Here is the same local finding with the uncertainty made explicit:\n\n" +
            localAnswer +
            "\n\nThe measured items above are evidence from this computer. They can show where a delay or problem occurred and identify possible contributors, but Sentinel should not turn a correlation into a confirmed root cause unless Windows or another local diagnostic records that connection directly.";

        private static string BuildDeterministicNextSteps(string localAnswer) =>
            "Based on the verified local evidence, Sentinel should perform any safe diagnostic it can verify itself before asking you to make broad manual changes. If a deeper local check is available for this type of issue, Sentinel will offer it. Review only items Sentinel or Windows specifically flags, prefer reversible changes, and re-check the result afterward. Sentinel has not changed anything automatically from this follow-up.\n\nCurrent verified finding:\n\n" +
            localAnswer;

        private static bool IsStartupLogonQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            bool startupTopic = value.Contains("log in") || value.Contains("login") || value.Contains("log on") ||
                                value.Contains("logon") || value.Contains("sign in") || value.Contains("signin") ||
                                value.Contains("sign-in") || value.Contains("boot") || value.Contains("startup") ||
                                value.Contains("start up") || value.Contains("starting windows");
            bool delay = value.Contains("slow") || value.Contains("takes") || value.Contains("taking") ||
                         value.Contains("long") || value.Contains("minutes") || value.Contains("delay") ||
                         value.Contains("hang") || value.Contains("stuck") || value.Contains("waiting");
            return startupTopic && delay;
        }

        private static string ExtractAskSentinelOriginalQuestion(string question)
        {
            int marker = question.LastIndexOf(AskSentinelOriginalQuestionMarker, StringComparison.Ordinal);
            if (marker < 0) return question.Trim();

            string original = question[(marker + AskSentinelOriginalQuestionMarker.Length)..].Trim();
            return string.IsNullOrWhiteSpace(original) ? question.Trim() : original;
        }

        private static string LimitFollowUpQuestion(string question)
        {
            const int maximumCharacters = 600;
            string value = question.Trim();
            return value.Length <= maximumCharacters
                ? value
                : value[..maximumCharacters] + "…";
        }

        private static string LimitFollowUpAnswer(string answer)
        {
            const int maximumCharacters = 1_600;
            string value = answer.Trim();
            return value.Length <= maximumCharacters
                ? value
                : value[..maximumCharacters] + "…";
        }
    }
}
