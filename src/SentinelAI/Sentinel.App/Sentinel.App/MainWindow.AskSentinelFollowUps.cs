using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        private readonly AskSentinelResolutionPlanner _askSentinelResolutionPlanner = new();
        private StackPanel? _askSentinelApprovalPanel;
        private Button? _askSentinelApprovalButton;
        private string _lastDeepStartupQuestion = string.Empty;
        private string _lastDeepStartupFinding = string.Empty;
        private string _askSentinelConversationQuestion = string.Empty;
        private string _askSentinelConversationLocalAnswer = string.Empty;
        private string _askSentinelConversationCurrentAnswer = string.Empty;
        private AskSentinelResolutionPlan? _askSentinelCurrentResolutionPlan;
        private bool _askSentinelResolutionReached;

        private void ResetAskSentinelConversationState(string question)
        {
            _askSentinelConversationQuestion = question.Trim();
            _askSentinelConversationLocalAnswer = string.Empty;
            _askSentinelConversationCurrentAnswer = string.Empty;
            _askSentinelCurrentResolutionPlan = null;
            _askSentinelResolutionReached = false;
            _lastDeepStartupQuestion = string.Empty;
            _lastDeepStartupFinding = string.Empty;
            AskSentinelNextStepsButton.IsEnabled = true;
            AskSentinelNextStepsButton.Content = "Next steps";
        }

        private void CaptureAskSentinelPrimaryAnswer(string question, string localAnswer, string displayedAnswer)
        {
            _askSentinelConversationQuestion = question.Trim();
            _askSentinelConversationLocalAnswer = localAnswer?.Trim() ?? string.Empty;
            _askSentinelConversationCurrentAnswer = displayedAnswer?.Trim() ?? string.Empty;
            _askSentinelCurrentResolutionPlan = null;
            _askSentinelResolutionReached = false;
            AskSentinelNextStepsButton.IsEnabled = true;
            AskSentinelNextStepsButton.Content = "Next steps";
        }

        private void CaptureAskSentinelFollowUpAnswer(string displayedAnswer, AskSentinelResolutionPlan? plan = null)
        {
            _askSentinelConversationCurrentAnswer = displayedAnswer?.Trim() ?? string.Empty;
            if (plan is null) return;

            _askSentinelCurrentResolutionPlan = plan;
            _askSentinelResolutionReached =
                plan.Disposition == AskSentinelResolutionDisposition.Resolved ||
                plan.Disposition == AskSentinelResolutionDisposition.NoActionNeeded ||
                plan.Disposition == AskSentinelResolutionDisposition.CannotRepairSafely;

            AskSentinelNextStepsButton.IsEnabled = !_askSentinelResolutionReached;
            AskSentinelNextStepsButton.Content = _askSentinelResolutionReached ? "Resolution reached" : "Next steps";
        }

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

            string original = !string.IsNullOrWhiteSpace(_askSentinelConversationQuestion)
                ? _askSentinelConversationQuestion
                : ExtractAskSentinelOriginalQuestion(current);
            string previousDisplayedAnswer = !string.IsNullOrWhiteSpace(_askSentinelConversationCurrentAnswer)
                ? _askSentinelConversationCurrentAnswer
                : AskSentinelAnswerText.Text?.Trim() ?? string.Empty;

            _askSentinelBusy = true;
            AskSentinelButton.IsEnabled = false;
            AskSentinelQuestionBox.IsEnabled = false;
            ClearAskSentinelResearchSources();
            HideAskSentinelRepairActions();
            HideAskSentinelApprovalAction();
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

                string localAnswer = !string.IsNullOrWhiteSpace(_askSentinelConversationLocalAnswer)
                    ? _askSentinelConversationLocalAnswer
                    : !string.IsNullOrWhiteSpace(localResponse.Answer)
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
                bool showDriverRepairActions = false;
                AskSentinelResolutionPlan? actionableApprovalPlan = null;
                AskSentinelResolutionPlan? resolutionPlan = null;

                if (action == AskSentinelFollowUpAction.SearchSources)
                {
                    AskSentinelProgressText.Text = "Keeping the local findings and checking current authoritative sources…";
                    ExternalInvestigationResult external = await _externalInvestigationGateway.InvestigateAsync(original, snapshot);

                    string externalAnswer;
                    if (external.RequiresSubscription)
                    {
                        externalAnswer = external.Summary +
                            "\n\nThe verified local findings above remain the active evidence for this computer.";
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

                    string searchContext = !string.IsNullOrWhiteSpace(_askSentinelConversationCurrentAnswer)
                        ? _askSentinelConversationCurrentAnswer
                        : !string.IsNullOrWhiteSpace(_lastDeepStartupFinding)
                            ? _lastDeepStartupFinding
                            : localAnswer;
                    answer =
                        "Current Sentinel finding:\n\n" + searchContext +
                        "\n\nCurrent authoritative-source research:\n\n" + externalAnswer;
                    insufficient = false;
                    provenance = AskSentinelProvenanceLabel.Advisory;
                    grounding = "Sentinel preserved the freshly re-checked local answer and added current authoritative-source research without treating external guidance as proof of this computer's state.";
                }
                else if (action == AskSentinelFollowUpAction.NextSteps)
                {
                    bool startupQuestion = IsStartupLogonQuestion(original);
                    bool hasStoredDeepStartupFinding = startupQuestion &&
                        string.Equals(_lastDeepStartupQuestion, original, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(_lastDeepStartupFinding);

                    string resolutionEvidence;
                    bool deepStartupCompleted = false;

                    if (startupQuestion && !hasStoredDeepStartupFinding)
                    {
                        AskSentinelProgressText.Text = "Running a deeper local startup and sign-in investigation…";
                        string deepFinding = await Task.Run(() => _askSentinelStartupLogonEvidence.GetDeepStartupLogonEvidence(snapshot));
                        _lastDeepStartupQuestion = original;
                        _lastDeepStartupFinding = deepFinding;
                        resolutionEvidence = deepFinding;
                        deepStartupCompleted = true;
                    }
                    else
                    {
                        resolutionEvidence = hasStoredDeepStartupFinding ? _lastDeepStartupFinding : localAnswer;
                        deepStartupCompleted = hasStoredDeepStartupFinding;
                    }

                    AskSentinelProgressText.Text = "Determining whether Sentinel can safely fix the verified finding…";
                    AskSentinelResolutionPlan plan = _askSentinelResolutionPlanner.CreatePlan(
                        original,
                        snapshot,
                        resolutionEvidence,
                        deepStartupCompleted);
                    resolutionPlan = plan;

                    if (plan.Disposition == AskSentinelResolutionDisposition.RepairCheckAvailable &&
                        plan.Action.Equals("driver-repair-check", StringComparison.OrdinalIgnoreCase))
                    {
                        _driverRepairDeviceName = GetDriverDeviceName(snapshot);
                        _preparedDriverRepairPlan = await _driverRepairCoordinator.PrepareAsync(_driverRepairDeviceName);
                        showDriverRepairActions = true;

                        answer = _preparedDriverRepairPlan.Available && _preparedDriverRepairPlan.AutomaticInstallationVerified
                            ? "Resolution decision\n\nSentinel found a driver repair path that passed the exact-device and signed-package checks. Use the repair controls below to review or explicitly approve the repair. Sentinel will not install it until you approve it, and it will verify the result afterward."
                            : "Resolution decision\n\nSentinel checked its dedicated driver-repair workflow. Use Continue Repair below to review the verified investigation result or official repair source. Sentinel will not install an unverified or merely similar driver package.";
                    }
                    else
                    {
                        answer = BuildResolutionAnswer(plan, resolutionEvidence);
                        if (plan.Disposition == AskSentinelResolutionDisposition.ApprovalRequired &&
                            snapshot.AutonomousProtectionRequiresUserApproval &&
                            !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionAction) &&
                            !snapshot.AutonomousProtectionAction.Equals("None", StringComparison.OrdinalIgnoreCase))
                        {
                            actionableApprovalPlan = plan;
                            answer += "\n\nUse Review & Approve below to inspect the exact action. Sentinel will revalidate the finding immediately before execution, and the approval is single-use.";
                        }
                    }

                    insufficient = false;
                    provenance = AskSentinelProvenanceLabel.Observed;
                    grounding = startupQuestion
                        ? "Sentinel completed the deeper startup/sign-in investigation and immediately converted that result into a terminal resolution decision using only supported remediation capabilities and exact current remediation state."
                        : "Sentinel converted the verified finding into a terminal resolution state using only supported remediation capabilities and exact current remediation state; no repair was invented from AI text.";
                }
                else
                {
                    AskSentinelProgressText.Text = "Explaining the local evidence in more detail…";

                    string explanationEvidence = !string.IsNullOrWhiteSpace(_askSentinelConversationCurrentAnswer)
                        ? _askSentinelConversationCurrentAnswer
                        : string.Equals(_lastDeepStartupQuestion, original, StringComparison.OrdinalIgnoreCase) &&
                          !string.IsNullOrWhiteSpace(_lastDeepStartupFinding)
                            ? _lastDeepStartupFinding
                            : localAnswer;

                    string followUpPrompt =
                        "Explain the current verified local answer in clearer, more detailed language. Separate measured facts, likely contributors, and anything that is still unknown. If Sentinel can perform a safe additional diagnostic itself, say so explicitly. Do not replace local evidence with generic web guidance. " +
                        $"Original question: {LimitFollowUpQuestion(original)} Current verified local answer: {LimitFollowUpAnswer(explanationEvidence)}";

                    SmartAiResult followUpAi = await _askSentinelAiCoordinator.AnalyzeAsync(
                        "ask-sentinel-explain-more",
                        followUpPrompt,
                        snapshot,
                        null,
                        _askSentinelRoutingPolicy.CreateBasicAiContext(followUpPrompt));

                    if (followUpAi.UsedCloudAi && !string.IsNullOrWhiteSpace(followUpAi.Answer))
                    {
                        answer = followUpAi.Answer.Trim();
                        insufficient = followUpAi.RequiresMoreEvidence;
                        provenance = AskSentinelProvenanceLabel.Advisory;
                        grounding = "Sentinel used AI only to explain the freshly re-checked local evidence in clearer language.";
                    }
                    else
                    {
                        answer = BuildDeterministicExplainMore(explanationEvidence);
                        insufficient = false;
                        grounding = "Sentinel expanded the freshly re-checked local answer without external research because AI interpretation was unavailable.";
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
                    HideAskSentinelRepairActions();
                    HideAskSentinelApprovalAction();
                    AskSentinelStatusText.Text = "The follow-up was blocked because it could not be grounded safely.";
                }
                else
                {
                    RenderAskSentinelAnswer(validation.Answer, displayCitations, displaySources);
                    CaptureAskSentinelFollowUpAnswer(validation.Answer, resolutionPlan);
                    if (showDriverRepairActions)
                    {
                        UpdateAskSentinelRepairActions(true, _preparedDriverRepairPlan);
                    }
                    else if (actionableApprovalPlan is not null)
                    {
                        ShowAskSentinelApprovalAction(actionableApprovalPlan);
                    }

                    AskSentinelStatusText.Text = action switch
                    {
                        AskSentinelFollowUpAction.SearchSources => "Kept the verified local findings and reported the current authoritative-source search result.",
                        AskSentinelFollowUpAction.NextSteps when showDriverRepairActions => "Sentinel prepared the driver repair workflow. Review or approve the repair below.",
                        AskSentinelFollowUpAction.NextSteps when actionableApprovalPlan is not null => "Sentinel found an exact supported action. Review and approve it below if you want Sentinel to proceed.",
                        AskSentinelFollowUpAction.NextSteps => "Sentinel completed the diagnostic and reached a final resolution decision from the current verified evidence.",
                        _ => "Expanded the answer from the current verified local evidence."
                    };
                }

                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                HideAskSentinelRepairActions();
                HideAskSentinelApprovalAction();
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

        private void ShowAskSentinelApprovalAction(AskSentinelResolutionPlan plan)
        {
            EnsureAskSentinelApprovalPanel();
            if (_askSentinelApprovalPanel is null || _askSentinelApprovalButton is null) return;

            _askSentinelApprovalButton.Content = "Review & Approve";
            ToolTipService.SetToolTip(
                _askSentinelApprovalButton,
                string.IsNullOrWhiteSpace(plan.Target)
                    ? "Review Sentinel's exact approved remediation before any change is made."
                    : $"Review the approved action for {plan.Target}. Sentinel will revalidate it before execution.");
            _askSentinelApprovalPanel.Visibility = Visibility.Visible;
        }

        private void EnsureAskSentinelApprovalPanel()
        {
            if (_askSentinelApprovalPanel is not null ||
                AskSentinelAnswerBorder.Child is not StackPanel answerStack)
                return;

            _askSentinelApprovalButton = new Button
            {
                Content = "Review & Approve",
                MinWidth = 155,
                Padding = new Thickness(16, 8, 16, 8),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            _askSentinelApprovalButton.Click += AskSentinelApprovalButton_Click;

            Button notNow = new()
            {
                Content = "Not Now",
                MinWidth = 95,
                Padding = new Thickness(14, 8, 14, 8)
            };
            notNow.Click += (_, _) =>
            {
                HideAskSentinelApprovalAction();
                AskSentinelStatusText.Text = "No repair was started. Sentinel will keep monitoring the finding.";
            };

            _askSentinelApprovalPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 16, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _askSentinelApprovalPanel.Children.Add(_askSentinelApprovalButton);
            _askSentinelApprovalPanel.Children.Add(notNow);
            answerStack.Children.Add(_askSentinelApprovalPanel);
        }

        private async void AskSentinelApprovalButton_Click(object sender, RoutedEventArgs e)
        {
            if (_askSentinelBusy) return;
            await ReviewApprovedRemediationAsync();
            await _engine.RefreshAsync();
            AskSentinelStatusText.Text = "Sentinel completed the approval workflow and refreshed the verified system state.";
            HideAskSentinelApprovalAction();
        }

        private void HideAskSentinelApprovalAction()
        {
            if (_askSentinelApprovalPanel is not null)
                _askSentinelApprovalPanel.Visibility = Visibility.Collapsed;
        }

        private static string BuildResolutionAnswer(
            AskSentinelResolutionPlan plan,
            string verifiedFinding)
        {
            string action = plan.Disposition switch
            {
                AskSentinelResolutionDisposition.Resolved => "Sentinel verified the repair outcome.",
                AskSentinelResolutionDisposition.NoActionNeeded => "Sentinel should not change the system right now.",
                AskSentinelResolutionDisposition.MoreDiagnosticsAvailable => "Sentinel can continue with a targeted diagnostic before any repair is considered.",
                AskSentinelResolutionDisposition.RepairAvailable => "A supported repair path is available.",
                AskSentinelResolutionDisposition.ApprovalRequired => "A supported action is available, but Sentinel requires your explicit approval before changing Windows.",
                AskSentinelResolutionDisposition.ManualReviewRequired => "The finding needs review before Sentinel can safely change the system.",
                AskSentinelResolutionDisposition.CannotRepairSafely => "Sentinel has reached the end of its safe automatic repair path for the current evidence.",
                _ => "Sentinel has completed the current resolution check."
            };

            string target = !string.IsNullOrWhiteSpace(plan.Target)
                ? $"\n\nTarget: {plan.Target}"
                : string.Empty;
            string exactAction = !string.IsNullOrWhiteSpace(plan.Action)
                ? $"\nAction: {plan.Action}"
                : string.Empty;

            return
                "Resolution decision\n\n" +
                plan.Title + "\n\n" +
                plan.Summary + "\n\n" +
                action + target + exactAction +
                "\n\nVerified finding used for this decision:\n" + verifiedFinding;
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
