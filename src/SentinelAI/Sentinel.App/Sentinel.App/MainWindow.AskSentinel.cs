using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Sentinel.App.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.System;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly AskSentinelResponseOrchestrator _askSentinelResponseOrchestrator = new();
        private readonly DriverAutomaticRepairCoordinator _driverRepairCoordinator = new();
        private bool _askSentinelBusy;
        private StackPanel? _askSentinelRepairPanel;
        private Button? _reviewRepairButton;
        private Button? _automaticRepairButton;
        private Button? _notNowButton;
        private string _driverRepairDeviceName = string.Empty;

        private async void AskSentinelButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelQuestionAsync();
        }

        private async void AskSentinelQuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await SubmitAskSentinelQuestionAsync();
        }

        private async Task SubmitAskSentinelQuestionAsync()
        {
            if (_askSentinelBusy)
            {
                return;
            }

            string question = AskSentinelQuestionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                AskSentinelStatusText.Text = "Type a question for Sentinel first.";
                AskSentinelAnswerBorder.Visibility = Visibility.Collapsed;
                HideAskSentinelRepairActions();
                return;
            }

            _askSentinelBusy = true;
            AskSentinelButton.IsEnabled = false;
            AskSentinelQuestionBox.IsEnabled = false;
            AskSentinelAnswerBorder.Visibility = Visibility.Collapsed;
            HideAskSentinelRepairActions();
            AskSentinelStatusText.Text = "Checking current verified evidence…";
            AskSentinelProgressText.Text = "Collecting verified local evidence…";
            AskSentinelProgressPanel.Visibility = Visibility.Visible;
            AskSentinelProgressRing.IsActive = true;

            try
            {
                await Task.Yield();
                await _engine.RefreshAsync();

                AskSentinelProgressText.Text = "Reviewing current evidence and investigation history…";
                var snapshot = _engine.CurrentSnapshot;
                var history = await _investigationHistoryService.ReadRecentAsync(100);

                AskSentinelProgressText.Text = "Preparing a verified answer…";
                AskSentinelResponseOrchestrator.AskSentinelResponse response = await Task.Run(() =>
                    _askSentinelResponseOrchestrator.CreateResponse(question, snapshot, history));

                AskSentinelAnswerText.Text = response.Answer;
                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
                UpdateAskSentinelRepairActions(response.Answer);
                AskSentinelStatusText.Text = response.IsInsufficientEvidence
                    ? $"Checked verified local evidence updated {response.EvidenceTimestamp:h:mm:ss tt}; Sentinel will not guess beyond it."
                    : response.UsedInvestigationHistory
                        ? $"Answered from verified current evidence and Sentinel investigation history; current evidence updated {response.EvidenceTimestamp:h:mm:ss tt}."
                        : $"Answered from verified local evidence updated {response.EvidenceTimestamp:h:mm:ss tt}.";
            }
            catch (Exception)
            {
                AskSentinelAnswerText.Text = "Sentinel could not refresh verified system evidence, so it will not guess at an answer.";
                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
                HideAskSentinelRepairActions();
                AskSentinelStatusText.Text = "Verified evidence is temporarily unavailable.";
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

        private void UpdateAskSentinelRepairActions(string answer)
        {
            bool driverRepairRelevant =
                answer.Contains("driver needs attention", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("a driver needs attention", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("driver health needs attention", StringComparison.OrdinalIgnoreCase);

            if (!driverRepairRelevant)
            {
                HideAskSentinelRepairActions();
                return;
            }

            _driverRepairDeviceName = ExtractDriverDeviceName(answer);
            EnsureAskSentinelRepairPanel();
            if (_askSentinelRepairPanel is null || _automaticRepairButton is null)
            {
                return;
            }

            _automaticRepairButton.Content = "Prepare Automatic Repair";
            _automaticRepairButton.IsEnabled = true;
            ToolTipService.SetToolTip(
                _automaticRepairButton,
                "Sentinel will first search Windows Update for a compatible signed driver. Nothing is installed until you review and approve the verified repair plan.");
            _askSentinelRepairPanel.Visibility = Visibility.Visible;
        }

        private void EnsureAskSentinelRepairPanel()
        {
            if (_askSentinelRepairPanel is not null)
            {
                return;
            }

            if (AskSentinelAnswerBorder.Child is not StackPanel answerStack)
            {
                return;
            }

            _reviewRepairButton = new Button
            {
                Content = "Review Repair",
                MinWidth = 128
            };
            _reviewRepairButton.Click += ReviewAskSentinelRepair_Click;

            _automaticRepairButton = new Button
            {
                Content = "Prepare Automatic Repair",
                MinWidth = 178
            };
            _automaticRepairButton.Click += AutomaticAskSentinelRepair_Click;

            _notNowButton = new Button
            {
                Content = "Not Now",
                MinWidth = 100
            };
            _notNowButton.Click += NotNowAskSentinelRepair_Click;

            _askSentinelRepairPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _askSentinelRepairPanel.Children.Add(_reviewRepairButton);
            _askSentinelRepairPanel.Children.Add(_automaticRepairButton);
            _askSentinelRepairPanel.Children.Add(_notNowButton);
            answerStack.Children.Add(_askSentinelRepairPanel);
        }

        private async void ReviewAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new()
            {
                Title = "Review driver repair",
                Content =
                    "Sentinel will search Windows Update for a compatible signed driver for the affected device. It will show you the exact package and source before downloading or installing anything.\n\n" +
                    "If you approve the verified plan, Sentinel will download and install the package through Windows Update. It will never restart the computer automatically. If a restart is required, Sentinel will tell you to save your work and wait for your approval.",
                CloseButtonText = "OK",
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void AutomaticAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            if (_automaticRepairButton is null || _askSentinelBusy)
            {
                return;
            }

            _askSentinelBusy = true;
            _automaticRepairButton.IsEnabled = false;
            AskSentinelProgressText.Text = "Searching Windows Update for a compatible signed driver…";
            AskSentinelProgressPanel.Visibility = Visibility.Visible;
            AskSentinelProgressRing.IsActive = true;

            try
            {
                DriverAutomaticRepairCoordinator.DriverRepairPlan plan =
                    await _driverRepairCoordinator.PrepareAsync(_driverRepairDeviceName);

                if (!plan.Available)
                {
                    ContentDialog unavailable = new()
                    {
                        Title = "Automatic repair is not available yet",
                        Content = plan.Summary,
                        CloseButtonText = "OK",
                        XamlRoot = ((FrameworkElement)Content).XamlRoot
                    };
                    await unavailable.ShowAsync();
                    AskSentinelStatusText.Text = "No repair was performed. Sentinel will continue investigating and monitoring the device.";
                    return;
                }

                ContentDialog approval = new()
                {
                    Title = "Approve driver repair",
                    Content =
                        $"Device: {plan.DeviceName}\n\n" +
                        $"Package: {plan.PackageTitle}\n" +
                        $"Source: {plan.Source}\n" +
                        $"Trust: {plan.TrustStatement}\n\n" +
                        plan.Summary,
                    PrimaryButtonText = "Download and Install",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = ((FrameworkElement)Content).XamlRoot
                };

                ContentDialogResult approvalResult = await approval.ShowAsync();
                if (approvalResult != ContentDialogResult.Primary)
                {
                    AskSentinelStatusText.Text = "Driver repair was not approved. No change was made.";
                    return;
                }

                AskSentinelProgressText.Text = "Downloading and installing the approved signed driver…";
                DriverAutomaticRepairCoordinator.DriverRepairResult result =
                    await _driverRepairCoordinator.ExecuteAsync(plan);

                if (!result.Success)
                {
                    ContentDialog failed = new()
                    {
                        Title = result.Title,
                        Content = result.Summary,
                        CloseButtonText = "OK",
                        XamlRoot = ((FrameworkElement)Content).XamlRoot
                    };
                    await failed.ShowAsync();
                    AskSentinelStatusText.Text = "The repair did not complete. No restart was requested.";
                    return;
                }

                if (result.RestartRequired)
                {
                    AskSentinelStatusText.Text = "The driver was installed. Save your work before restarting; Sentinel will verify the repair after Windows starts again.";
                    ContentDialog restartDialog = new()
                    {
                        Title = "Driver installed — restart required",
                        Content =
                            result.Summary +
                            "\n\nPlease save any open work now. Sentinel will restart Windows only if you select Restart Now.",
                        PrimaryButtonText = "Restart Now",
                        SecondaryButtonText = "Restart Later",
                        DefaultButton = ContentDialogButton.Secondary,
                        XamlRoot = ((FrameworkElement)Content).XamlRoot
                    };

                    ContentDialogResult restartChoice = await restartDialog.ShowAsync();
                    if (restartChoice == ContentDialogResult.Primary)
                    {
                        AskSentinelStatusText.Text = "Restart approved. Sentinel will verify the driver after Windows starts again.";
                        RestartWindowsNow();
                    }
                    else
                    {
                        AskSentinelStatusText.Text = "Restart postponed. Save your work and restart when convenient; Sentinel will verify the driver after startup.";
                    }
                    return;
                }

                ContentDialog complete = new()
                {
                    Title = result.Title,
                    Content = result.Summary,
                    CloseButtonText = "OK",
                    XamlRoot = ((FrameworkElement)Content).XamlRoot
                };
                await complete.ShowAsync();
                AskSentinelStatusText.Text = "The driver was installed. Sentinel is refreshing local evidence to verify the repair.";
                await UpdateDashboardAsync();
            }
            finally
            {
                AskSentinelProgressRing.IsActive = false;
                AskSentinelProgressPanel.Visibility = Visibility.Collapsed;
                _askSentinelBusy = false;
                _automaticRepairButton.IsEnabled = true;
            }
        }

        private static void RestartWindowsNow()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/r /t 0",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
                // If Windows rejects the restart request, Sentinel leaves the
                // computer running. The user can still restart manually.
            }
        }

        private void NotNowAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            HideAskSentinelRepairActions();
            AskSentinelStatusText.Text = "No repair was started. Sentinel will continue monitoring this condition.";
        }

        private void HideAskSentinelRepairActions()
        {
            _driverRepairDeviceName = string.Empty;
            if (_askSentinelRepairPanel is not null)
            {
                _askSentinelRepairPanel.Visibility = Visibility.Collapsed;
            }
        }

        private static string ExtractDriverDeviceName(string answer)
        {
            string[] lines = answer.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim().Equals("What I found", StringComparison.OrdinalIgnoreCase))
                {
                    string device = lines[i + 1].Trim();
                    int codeIndex = device.LastIndexOf("(Code ", StringComparison.OrdinalIgnoreCase);
                    return codeIndex > 0 ? device[..codeIndex].Trim() : device;
                }
            }
            return "Affected Windows device";
        }
    }
}
