using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Sentinel.App.Services;
using System;
using System.Threading.Tasks;
using Windows.System;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly AskSentinelResponseOrchestrator _askSentinelResponseOrchestrator = new();
        private bool _askSentinelBusy;
        private StackPanel? _askSentinelRepairPanel;
        private Button? _reviewRepairButton;
        private Button? _automaticRepairButton;
        private Button? _notNowButton;

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

            EnsureAskSentinelRepairPanel();
            if (_askSentinelRepairPanel is null || _automaticRepairButton is null)
            {
                return;
            }

            _automaticRepairButton.IsEnabled = false;
            ToolTipService.SetToolTip(
                _automaticRepairButton,
                "Sentinel will enable automatic repair only after the Investigation Engine verifies a signed, compatible driver package and a reversible installation plan.");
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
                Content = "Repair Automatically",
                MinWidth = 154,
                IsEnabled = false
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
                    "Sentinel will not install a driver until it verifies a signed package from Windows Update or the computer manufacturer, confirms device compatibility, records a reversible repair plan, and determines whether a restart is required.\n\n" +
                    "When the verified repair is ready, Sentinel will ask for approval before downloading or installing anything. It will then tell you when to save your work before any restart.",
                CloseButtonText = "OK",
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void AutomaticAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new()
            {
                Title = "Automatic repair is not yet verified",
                Content = "Sentinel has not yet verified a compatible signed driver package and reversible installation plan for this device. No change was made.",
                CloseButtonText = "OK",
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void NotNowAskSentinelRepair_Click(object sender, RoutedEventArgs e)
        {
            HideAskSentinelRepairActions();
            AskSentinelStatusText.Text = "No repair was started. Sentinel will continue monitoring this condition.";
        }

        private void HideAskSentinelRepairActions()
        {
            if (_askSentinelRepairPanel is not null)
            {
                _askSentinelRepairPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
