using Microsoft.UI.Xaml;
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
                return;
            }

            _askSentinelBusy = true;
            AskSentinelButton.IsEnabled = false;
            AskSentinelQuestionBox.IsEnabled = false;
            AskSentinelStatusText.Text = "Checking current verified evidence…";

            try
            {
                await _engine.RefreshAsync();
                var snapshot = _engine.CurrentSnapshot;
                AskSentinelResponseOrchestrator.AskSentinelResponse response =
                    _askSentinelResponseOrchestrator.CreateResponse(question, snapshot);

                AskSentinelAnswerText.Text = response.Answer;
                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
                AskSentinelStatusText.Text = response.IsInsufficientEvidence
                    ? $"Checked verified local evidence updated {response.EvidenceTimestamp:h:mm:ss tt}; Sentinel will not guess beyond it."
                    : $"Answered from verified local evidence updated {response.EvidenceTimestamp:h:mm:ss tt}.";
            }
            catch (Exception)
            {
                AskSentinelAnswerText.Text = "Sentinel could not refresh verified system evidence, so it will not guess at an answer.";
                AskSentinelAnswerBorder.Visibility = Visibility.Visible;
                AskSentinelStatusText.Text = "Verified evidence is temporarily unavailable.";
            }
            finally
            {
                _askSentinelBusy = false;
                AskSentinelButton.IsEnabled = true;
                AskSentinelQuestionBox.IsEnabled = true;
                AskSentinelQuestionBox.Focus(FocusState.Programmatic);
            }
        }
    }
}
