using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Sentinel.App.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly AskSentinelResponseOrchestrator _askSentinelResponseOrchestrator = new();
        private readonly AskSentinelResponseSafetyValidator _askSentinelResponseSafetyValidator = new();
        private readonly ExternalInvestigationGateway _externalInvestigationGateway = new();
        private readonly SmartSentinelAiCoordinator _askSentinelAiCoordinator = new();
        private readonly DriverAutomaticRepairCoordinator _driverRepairCoordinator = new();
        private readonly MaintenanceOutcomeRecorder _askSentinelOutcomeRecorder = new();
        private bool _askSentinelBusy;
        private StackPanel? _askSentinelRepairPanel;
        private Button? _reviewRepairButton;
        private Button? _automaticRepairButton;
        private Button? _notNowButton;
        private string _driverRepairDeviceName = string.Empty;
        private DriverAutomaticRepairCoordinator.DriverRepairPlan? _preparedDriverRepairPlan;

        private async void AskSentinelButton_Click(object sender, RoutedEventArgs e) => await SubmitAskSentinelQuestionAsync();

        private async void AskSentinelQuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            await SubmitAskSentinelQuestionAsync();
        }

        private async Task SubmitAskSentinelQuestionAsync()
        {
            if (_askSentinelBusy) return;
            string question = AskSentinelQuestionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                AskSentinelStatusText.Text = "Type a question for Sentinel first.";
                AskSentinelAnswerBorder.Visibility = Visibility.Collapsed;
                HideAskSentinelRepairActions();
                return;
            }

            _askSentinelBusy = true;
            _preparedDriverRepairPlan = null;
            AskSentinelButton.IsEnabled = false;
            AskSentinelQuestionBox.IsEnabled = false;
            AskSentinelAnswerBorder.Visibility = Visibility.Collapsed;
            HideAskSentinelRepairActions();
            AskSentinelStatusText.Text = "Checking this computer…";
            AskSentinelProgressText.Text = "Checking local evidence…";
            AskSentinelProgressPanel.Visibility = Visibility.Visible;
            AskSentinelProgressRing.IsActive = true;

            try
            {
                await Task.Yield();
                await _engine.RefreshAsync();
                var snapshot = _engine.CurrentSnapshot;
                var history = await _investigationHistoryService.ReadRecentAsync(100);
                bool optimizationQuestion = IsOptimizationQuestion(question);

                if (optimizationQuestion)
                {
                    AskSentinelProgressText.Text = "Checking current optimization status…";
                    AutomaticOptimizationResult optimization = await _automaticOptimizationCoordinator.EvaluateAndRunAsync(snapshot);
                    UpdateOptimizationStatus(optimization);
                }

                AskSentinelProgressText.Text = "Reviewing what Sentinel already knows…";
                AskSentinelResponseOrchestrator.AskSentinelResponse response = await Task.Run(() =>
                    _askSentinelResponseOrchestrator.CreateResponse(question, snapshot, history));
                AskSentinelProvenanceLabel responseProvenance = response.UsedInvestigationHistory
                    ? AskSentinelProvenanceLabel.VerifiedFact
                    : AskSentinelProvenanceLabel.Observed;

                if (optimizationQuestion && IsCurrentOptimizationStatusVerifiedHealthy())
                {
                    response = response with
                    {
                        Answer = "Sentinel analyzed this computer and found that it is running at optimal performance. No performance optimization is needed right now.",
                        IsInsufficientEvidence = false,
                        UsedInvestigationHistory = true,
                        PassedFinalSafetyValidation = false,
                        GroundingSummary = "Answer grounded in Sentinel's current verified automatic optimization status and maintenance history."
                    };
                    responseProvenance = AskSentinelProvenanceLabel.VerifiedFact;
                }

                bool crashQuestion = IsCrashQuestion(question);
                bool driverIssue = !optimizationQuestion && !crashQuestion && IsDriverIssue(question, snapshot, response.Answer);
                SmartAiResult? basicAi = null;

                if (response.IsInsufficientEvidence && !RequiresFreshExternalResearch(question))
                {
                    AskSentinelProgressText.Text = "Using Sentinel AI to understand your question…";
                    AiEscalationContext basicContext = CreateBasicAskSentinelAiContext(question);
                    basicAi = await _askSentinelAiCoordinator.AnalyzeAsync(
                        "ask-sentinel-basic",
                        question,
                        snapshot,
                        null,
                        basicContext);

                    if (basicAi.UsedCloudAi && !string.IsNullOrWhiteSpace(basicAi.Answer))
                    {
                        response = response with
                        {
                            Answer = basicAi.Answer,
                            IsInsufficientEvidence = basicAi.RequiresMoreEvidence,
                            PassedFinalSafetyValidation = false,
                            GroundingSummary = basicAi.FromCache
                                ? "Sentinel reused a recent Basic AI interpretation of the same redacted verified evidence."
                                : "Sentinel used Basic AI to interpret the user's question against redacted verified local evidence."
                        };
                        responseProvenance = AskSentinelProvenanceLabel.Advisory;
                        driverIssue = !optimizationQuestion && !crashQuestion && IsDriverIssue(question, snapshot, response.Answer);
                    }
                }

                if (response.IsInsufficientEvidence)
                {
                    AskSentinelProgressText.Text = "Checking approved sources…";
                    ExternalInvestigationResult external = await _externalInvestigationGateway.InvestigateAsync(question, snapshot);

                    if (external.RequiresSubscription)
                    {
                        driverIssue = false;
                        string answer = basicAi?.UsedCloudAi == true && !string.IsNullOrWhiteSpace(basicAi.Answer)
                            ? basicAi.Answer + "\n\nI can also check approved external sources and use Advanced AI for deeper investigation when the subscription is active."
                            : BuildFreeExternalResearchFallback(external, snapshot);
                        response = response with
                        {
                            Answer = answer,
                            IsInsufficientEvidence = false,
                            PassedFinalSafetyValidation = false,
                            GroundingSummary = "Sentinel returned available free local/Basic AI help; approved external research and Advanced AI require the paid entitlement."
                        };
                        responseProvenance = AskSentinelProvenanceLabel.Advisory;
                    }
                    else if (driverIssue)
                    {
                        _driverRepairDeviceName = GetDriverDeviceName(snapshot);
                        AskSentinelProgressText.Text = "I found the issue. Checking for a safe repair…";
                        _preparedDriverRepairPlan = await _driverRepairCoordinator.PrepareAsync(_driverRepairDeviceName);
                        response = response with
                        {
                            Answer = BuildDriverConsumerAnswer(external, _preparedDriverRepairPlan),
                            PassedFinalSafetyValidation = false,
                            GroundingSummary = "Sentinel combined verified local driver evidence, approved external research, and a locally verified repair check."
                        };
                        responseProvenance = AskSentinelProvenanceLabel.Inferred;
                    }
                    else
                    {
                        SmartAiResult externalAi = await _askSentinelAiCoordinator.AnalyzeAsync(
                            "external-investigation",
                            question,
                            snapshot,
                            external,
                            CreateExternalAskSentinelAiContext(question, external));

                        string externalAnswer = externalAi.UsedCloudAi && !string.IsNullOrWhiteSpace(externalAi.Answer)
                            ? externalAi.Answer
                            : BuildConsumerExternalAnswer(external);
                        response = response with
                        {
                            Answer = externalAnswer,
                            IsInsufficientEvidence = externalAi.UsedCloudAi ? externalAi.RequiresMoreEvidence : !external.Verified,
                            PassedFinalSafetyValidation = false,
                            GroundingSummary = externalAi.UsedCloudAi
                                ? "Sentinel combined verified local evidence, bounded approved-source passages, and AI interpretation."
                                : external.Verified
                                    ? "Sentinel combined verified local evidence with approved authoritative research."
                                    : "Sentinel checked approved sources but did not find enough verified information to make a stronger claim."
                        };
                        responseProvenance = external.Verified
                            ? AskSentinelProvenanceLabel.Inferred
                            : AskSentinelProvenanceLabel.Advisory;
                    }

                    string sourceNames = external.Sources.Count == 0
                        ? "approved authoritative sources"
                        : string.Join(", ", external.Sources.Select(x => x.SourceName).Distinct());
                    string fingerprint = $"external:{external.Topic}:{question.Trim().ToLowerInvariant()}";
                    await _investigationHistoryService.RecordAsync(fingerprint, "External investigation", external.Summary,
                        external.Verified ? "Information" : "Attention", external.RequiresAiEscalation, false);
                    _askSentinelOutcomeRecorder.RecordInvestigation("External investigation", external.Summary,
                        external.RequiresAiEscalation,
                        $"Topic: {external.Topic}; Confidence: {external.ConfidencePercent}%; Sources: {sourceNames}");
                    UpdateMaintenanceReport();
                }

                AskSentinelResponseSafetyValidator.ValidationResult finalValidation =
                    _askSentinelResponseSafetyValidator.ValidateForDisplay(response, snapshot, responseProvenance);
                if (!finalValidation.IsSafe)
                {
                    response = response with
                    {
                        Answer = finalValidation.Answer,
                        IsInsufficientEvidence = true,
                        UsedInvestigationHistory = false,
                        UsedRecommendationGuard = false,
                        PassedFinalSafetyValidation = true,
                        GroundingSummary = $"Final display validation blocked the composed response: {finalValidation.Reason}"
                    };
                    driverIssue = false;
                }
                else
                {
                    response = response with
                    {
                        Answer = finalValidation.Answer,
                        PassedFinalSafetyValidation = true,
                        GroundingSummary = $"{finalValidation.Provenance}: {response.GroundingSummary}"
                    };
                }

                AskSentinelAnswerText.Text = response.Answer;
                AskSentinelAnswerText.FontSize = 17;
                AskSentinelAnswerText.LineHeight = 25;
                AskSentinelAnswerBorder.Padding = new Thickness(20);
                AskSentinelAnswerBorder.CornerRadius = new CornerRadius(12);
                AskSentinelAnswerBorder.Visibility = Visibility.Visible;

                if (driverIssue)
                {
                    if (string.IsNullOrWhiteSpace(_driverRepairDeviceName)) _driverRepairDeviceName = GetDriverDeviceName(snapshot);
                    UpdateAskSentinelRepairActions(true, _preparedDriverRepairPlan);
                }
                else HideAskSentinelRepairActions();

                AskSentinelStatusText.Text = response.IsInsufficientEvidence
                    ? "Sentinel checked local evidence, available AI help, and approved sources where entitled."
                    : response.UsedInvestigationHistory
                        ? "Answered from current evidence and Sentinel's verified investigation history."
                        : responseProvenance == AskSentinelProvenanceLabel.Advisory
                            ? "Answered with Sentinel AI using bounded local evidence and available approved research."
                            : "Answered from current verified evidence on this computer.";
            }
            catch (Exception)
            {
                AskSentinelAnswerText.Text = "I couldn't finish checking the evidence, so I won't guess. I can still answer another question or try the investigation again.";
                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
                HideAskSentinelRepairActions();
                AskSentinelStatusText.Text = "Verified evidence or AI assistance is temporarily unavailable.";
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

        private bool IsCurrentOptimizationStatusVerifiedHealthy() =>
            _optimizationStatusSummary.Contains("No verified performance optimization is needed", StringComparison.OrdinalIgnoreCase) ||
            _optimizationStatusSummary.Contains("performance is within this computer's established baseline", StringComparison.OrdinalIgnoreCase);

        private static AiEscalationContext CreateBasicAskSentinelAiContext(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            bool highRisk = ContainsAny(value, "security", "malware", "virus", "ransomware", "spyware", "hack", "breach", "firewall", "credential", "password");
            bool highComplexity = question.Length > 180 || ContainsAny(value, "compare", "analyze", "root cause", "why", "explain", "multiple", "several");
            return new AiEscalationContext(
                LocalEvidenceAvailable: true,
                LocalEvidenceInsufficient: true,
                LocalConclusionVerified: false,
                CachedVerifiedFindingAvailable: false,
                ExternalResearchApplicable: false,
                AuthoritativeResearchAttempted: false,
                AuthoritativeExternalConclusionVerified: false,
                NeedsInterpretation: true,
                NeedsUserExplanation: true,
                HighComplexity: highComplexity,
                HighRisk: highRisk);
        }

        private static AiEscalationContext CreateExternalAskSentinelAiContext(string question, ExternalInvestigationResult external)
        {
            string value = question.Trim().ToLowerInvariant();
            bool highRisk = external.Topic.Equals("security", StringComparison.OrdinalIgnoreCase) ||
                            external.Topic.Equals("firewall", StringComparison.OrdinalIgnoreCase) ||
                            ContainsAny(value, "malware", "virus", "ransomware", "breach", "hack");
            return new AiEscalationContext(
                LocalEvidenceAvailable: true,
                LocalEvidenceInsufficient: true,
                LocalConclusionVerified: false,
                CachedVerifiedFindingAvailable: false,
                ExternalResearchApplicable: true,
                AuthoritativeResearchAttempted: true,
                AuthoritativeExternalConclusionVerified: external.Verified,
                NeedsInterpretation: true,
                NeedsUserExplanation: true,
                HighComplexity: external.Sources.Count > 1 || question.Length > 180,
                HighRisk: highRisk);
        }

        private static bool RequiresFreshExternalResearch(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return ContainsAny(value,
                "search online", "search the internet", "look online", "look it up", "external source", "external sources",
                "authoritative source", "official source", "official documentation", "microsoft says", "vendor says", "manufacturer says",
                "latest", "current version", "current release", "release notes", "known issue", "known issues", "cve", "security advisory",
                "research this", "check online", "check the internet");
        }

        private static string BuildFreeExternalResearchFallback(ExternalInvestigationResult external, dynamic snapshot)
        {
            string local = snapshot.InvestigationRequiresAttention
                ? $"From this computer's verified local evidence, Sentinel is currently reporting: {snapshot.InvestigationSummary}"
                : $"From this computer's verified local evidence, Sentinel does not currently report a condition requiring attention. Defender is {snapshot.DefenderStatus} and Firewall is {snapshot.FirewallStatus}.";
            return $"{local}\n\n{external.Summary}\n\nYou can still ask me about the local evidence, what a Windows feature means, how to interpret a setting or error, or what information I need next. Approved external research and Advanced AI require the paid subscription.";
        }

        private static bool ContainsAny(string value, params string[] terms) =>
            terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

        private static bool IsOptimizationQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return value.Contains("optimization") || value.Contains("optimizations") || value.Contains("optimize") ||
                   value.Contains("optimized") || value.Contains("performance maintenance") || value.Contains("defrag") || value.Contains("retrim");
        }

        private static string BuildDriverConsumerAnswer(ExternalInvestigationResult external, DriverAutomaticRepairCoordinator.DriverRepairPlan plan)
        {
            bool identified = !string.IsNullOrWhiteSpace(plan.DeviceName) &&
                              !plan.DeviceName.Equals("Affected Windows device", StringComparison.OrdinalIgnoreCase);
            string first = identified
                ? $"I found a driver problem\n\n{plan.DeviceName} is reporting a problem."
                : "I found evidence of a driver-related problem, but I cannot yet identify the exact device reliably.";
            string finding = external.Verified
                ? "I checked this computer's driver, system information, recent Windows events, and approved Microsoft driver sources. The evidence points most strongly to a driver or firmware compatibility problem."
                : "I checked this computer's driver and system information. I do not have enough verified evidence yet to name one exact cause safely.";
            string action = plan.Available && plan.AutomaticInstallationVerified
                ? "I found a Microsoft-signed driver package that Sentinel can install. Nothing will change until you approve the repair."
                : plan.ResearchPerformed && !string.IsNullOrWhiteSpace(plan.Source)
                    ? $"I couldn't verify a safe automatic package yet, but I found the official next repair source: {plan.Source}. I can help you continue from there."
                    : "I couldn't verify a safe automatic repair yet. I will not install an unverified driver.";
            return $"{first}\n\nWhat I found\n{finding}\n\nWhat I can do\n{action}";
        }

        private static string BuildConsumerExternalAnswer(ExternalInvestigationResult external)
        {
            if (!external.Verified)
                return external.Sources.Count > 0
                    ? external.Summary + "\n\nI found approved-source material, but I do not have enough support to turn it into a stronger machine-specific conclusion."
                    : "I checked approved external sources, but I don't have enough verified information to give you a reliable machine-specific answer yet. I won't guess.";
            return external.Summary;
        }

        private static bool IsCrashQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return value.Contains("blue screen") ||
                   value.Contains("blue-screen") ||
                   value.Contains("bsod") ||
                   value.Contains("bugcheck") ||
                   value.Contains("bug check") ||
                   value.Contains("stop code") ||
                   value.Contains("system crash");
        }

        private static bool IsDriverIssue(string question, dynamic snapshot, string answer)
        {
            string value = (question + " " + (snapshot.InvestigationReasonCode ?? string.Empty) + " " + (snapshot.InvestigationConclusion ?? string.Empty) + " " + (snapshot.InvestigationSummary ?? string.Empty) + " " + answer).ToLowerInvariant();
            return value.Contains("driver") || value.Contains("management engine") || value.Contains("code 10") || value.Contains("device manager");
        }

        private static string GetDriverDeviceName(dynamic snapshot)
        {
            string combined = ((snapshot.InvestigationConclusion ?? string.Empty) + " " + (snapshot.InvestigationSummary ?? string.Empty) + " " + (snapshot.GuidanceEvidence ?? string.Empty));
            if (combined.Contains("Intel(R) Management Engine Interface", StringComparison.OrdinalIgnoreCase)) return "Intel(R) Management Engine Interface";
            if (combined.Contains("Intel Management Engine Interface", StringComparison.OrdinalIgnoreCase)) return "Intel Management Engine Interface";
            if (combined.Contains("Management Engine Interface", StringComparison.OrdinalIgnoreCase)) return "Management Engine Interface";
            return "Affected Windows device";
        }

        private void UpdateAskSentinelRepairActions(bool relevant, DriverAutomaticRepairCoordinator.DriverRepairPlan? plan)
        {
            if (!relevant) { HideAskSentinelRepairActions(); return; }
            EnsureAskSentinelRepairPanel();
            if (_askSentinelRepairPanel is null || _automaticRepairButton is null || _reviewRepairButton is null) return;
            _reviewRepairButton.Content = "Details";
            _reviewRepairButton.MinWidth = 110;
            _automaticRepairButton.Content = plan?.Available == true && plan.AutomaticInstallationVerified ? "Repair Automatically" : "Continue Repair";
            _automaticRepairButton.MinWidth = 170;
            _automaticRepairButton.IsEnabled = true;
            _automaticRepairButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            _automaticRepairButton.Padding = new Thickness(18, 9, 18, 9);
            ToolTipService.SetToolTip(_automaticRepairButton, plan?.Available == true ? "Review and approve the verified Microsoft-signed repair." : "Continue Sentinel's verified repair investigation. No unverified software will be installed.");
            _askSentinelRepairPanel.Margin = new Thickness(0, 16, 0, 0);
            _askSentinelRepairPanel.Visibility = Visibility.Visible;
        }

        private void EnsureAskSentinelRepairPanel()
        {
            if (_askSentinelRepairPanel is not null || AskSentinelAnswerBorder.Child is not StackPanel answerStack) return;
            _reviewRepairButton = new Button { Content = "Details", MinWidth = 110 };
            _reviewRepairButton.Click += ReviewAskSentinelRepair_Click;
            _automaticRepairButton = new Button { Content = "Continue Repair", MinWidth = 170 };
            _automaticRepairButton.Click += AutomaticAskSentinelRepair_Click;
            _notNowButton = new Button { Content = "Not Now", MinWidth = 100 };
            _notNowButton.Click += NotNowAskSentinelRepair_Click;
            _askSentinelRepairPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 16, 0, 0), Visibility = Visibility.Collapsed };
            _askSentinelRepairPanel.Children.Add(_automaticRepairButton);
            _askSentinelRepairPanel.Children.Add(_reviewRepairButton);
            _askSentinelRepairPanel.Children.Add(_notNowButton);
            answerStack.Children.Add(_askSentinelRepairPanel);
        }

        private async void ReviewAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            string details = _preparedDriverRepairPlan is null
                ? "Sentinel detected a driver-related problem and is using verified local evidence plus approved Microsoft and manufacturer sources to determine a safe repair."
                : $"Device: {_preparedDriverRepairPlan.DeviceName}\n\nSource: {(_preparedDriverRepairPlan.Source.Length == 0 ? "Still investigating" : _preparedDriverRepairPlan.Source)}\n\n{_preparedDriverRepairPlan.Summary}" + (string.IsNullOrWhiteSpace(_preparedDriverRepairPlan.DiagnosticEvidence) ? string.Empty : $"\n\nTechnical evidence\n{_preparedDriverRepairPlan.DiagnosticEvidence}");
            ContentDialog dialog = new() { Title = "Repair details", Content = details, CloseButtonText = "Close", XamlRoot = ((FrameworkElement)Content).XamlRoot };
            await dialog.ShowAsync();
        }

        private async void AutomaticAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            if (_automaticRepairButton is null || _askSentinelBusy) return;

            SubscriptionState subscription = await new StoreSubscriptionService().GetStateAsync();
            if (!subscription.IsActive)
            {
                ContentDialog required = new()
                {
                    Title = "Subscription required",
                    Content = "Free local driver monitoring remains active. An active Sentinel AI subscription is required to continue a repair or install a verified driver package.",
                    CloseButtonText = "OK",
                    XamlRoot = ((FrameworkElement)Content).XamlRoot
                };
                await required.ShowAsync();
                return;
            }

            _askSentinelBusy = true;
            _automaticRepairButton.IsEnabled = false;
            AskSentinelProgressText.Text = "Preparing a verified repair…";
            AskSentinelProgressPanel.Visibility = Visibility.Visible;
            AskSentinelProgressRing.IsActive = true;
            try
            {
                DriverAutomaticRepairCoordinator.DriverRepairPlan plan = _preparedDriverRepairPlan ?? await _driverRepairCoordinator.PrepareAsync(_driverRepairDeviceName);
                _preparedDriverRepairPlan = plan;
                string fingerprint = $"driver:{_driverRepairDeviceName.Trim().ToLowerInvariant()}";
                if (!plan.Available)
                {
                    await _investigationHistoryService.RecordAsync(fingerprint, "Driver repair investigation", plan.Summary, "Attention", true, false);
                    _askSentinelOutcomeRecorder.RecordInvestigation("Driver repair investigation", $"Sentinel investigated {_driverRepairDeviceName} but did not verify an automatically installable repair.", true, plan.Summary);
                    UpdateMaintenanceReport();
                    string message = !string.IsNullOrWhiteSpace(plan.Source) ? $"I couldn't verify a safe automatic package yet. The official next source is {plan.Source}." : "I couldn't verify a safe automatic repair yet, so I did not install anything.";
                    ContentDialog researched = new() { Title = "No automatic repair verified yet", Content = message, PrimaryButtonText = string.IsNullOrWhiteSpace(plan.SourceUri) ? string.Empty : "Open Official Source", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close, XamlRoot = ((FrameworkElement)Content).XamlRoot };
                    ContentDialogResult researchChoice = await researched.ShowAsync();
                    if (researchChoice == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(plan.SourceUri)) OpenOfficialSource(plan.SourceUri);
                    AskSentinelStatusText.Text = "Sentinel did not install anything because a safe automatic repair was not verified.";
                    return;
                }
                ContentDialog approval = new() { Title = "Repair this driver?", Content = $"Sentinel found a verified Microsoft-signed driver package for {_driverRepairDeviceName}.\n\nSource: {plan.Source}\nPackage: {plan.PackageTitle}\n\nSentinel will install only this verified package. A restart, if needed, requires separate approval.", PrimaryButtonText = "Repair Automatically", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = ((FrameworkElement)Content).XamlRoot };
                if (await approval.ShowAsync() != ContentDialogResult.Primary) { AskSentinelStatusText.Text = "Repair canceled. No change was made."; return; }
                AskSentinelProgressText.Text = "Installing the verified repair…";
                DriverAutomaticRepairCoordinator.DriverRepairResult result = await _driverRepairCoordinator.ExecuteAsync(plan);
                await _investigationHistoryService.RecordAsync(fingerprint, "Driver repair", result.Summary, result.Success ? "Resolved" : "Attention", !result.Success, result.Success);
                _askSentinelOutcomeRecorder.RecordVerificationResult("Driver repair", result.Summary, result.Success, $"Device: {plan.DeviceName}; Package: {plan.PackageTitle}; Source: {plan.Source}; Restart required: {result.RestartRequired}");
                UpdateMaintenanceReport();
                if (!result.Success) { ContentDialog failed = new() { Title = "Repair did not complete", Content = result.Summary, CloseButtonText = "Close", XamlRoot = ((FrameworkElement)Content).XamlRoot }; await failed.ShowAsync(); AskSentinelStatusText.Text = "The repair did not complete. No restart was requested."; return; }
                if (result.RestartRequired)
                {
                    ContentDialog restartDialog = new() { Title = "Repair installed — restart required", Content = "The verified driver repair was installed successfully. Windows needs to restart to finish applying it. Restart now?", PrimaryButtonText = "Restart Now", CloseButtonText = "Later", DefaultButton = ContentDialogButton.Close, XamlRoot = ((FrameworkElement)Content).XamlRoot };
                    if (await restartDialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        _askSentinelOutcomeRecorder.RecordVerificationResult("Restart approved", "You approved the restart required to finish the verified driver repair.", true, "The restart was requested only after the repair completed and you approved it.");
                        UpdateMaintenanceReport();
                        Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/r /t 0", UseShellExecute = true });
                    }
                    else AskSentinelStatusText.Text = "Repair installed successfully. Restart later to finish applying it.";
                }
                else
                {
                    ContentDialog completed = new() { Title = "Repair complete", Content = "The verified driver repair completed successfully. No restart is required.", CloseButtonText = "Done", XamlRoot = ((FrameworkElement)Content).XamlRoot };
                    await completed.ShowAsync();
                    AskSentinelStatusText.Text = "Repair completed and verified.";
                }
            }
            catch { AskSentinelStatusText.Text = "Sentinel could not complete the repair safely, so it stopped without making an unverified change."; }
            finally
            {
                AskSentinelProgressRing.IsActive = false;
                AskSentinelProgressPanel.Visibility = Visibility.Collapsed;
                _askSentinelBusy = false;
                if (_automaticRepairButton is not null) _automaticRepairButton.IsEnabled = true;
            }
        }

        private void NotNowAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            HideAskSentinelRepairActions();
            AskSentinelStatusText.Text = "No repair was started. Sentinel will keep monitoring this issue.";
        }

        private void HideAskSentinelRepairActions()
        {
            if (_askSentinelRepairPanel is not null) _askSentinelRepairPanel.Visibility = Visibility.Collapsed;
        }

        private static void OpenOfficialSource(string sourceUri)
        {
            if (Uri.TryCreate(sourceUri, UriKind.Absolute, out Uri? uri)) _ = Launcher.LaunchUriAsync(uri);
        }
    }
}
