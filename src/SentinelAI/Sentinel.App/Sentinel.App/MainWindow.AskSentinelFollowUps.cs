using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private const string AskSentinelOriginalQuestionMarker = "\nOriginal question: ";

        private async void AskSentinelExplainMoreButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelFollowUpAsync(
                "Explain this in more detail in clear language. If you mention this computer specifically, use only verified local evidence.");
        }

        private async void AskSentinelSearchSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelFollowUpAsync(
                "Search current authoritative sources for this question and explain what the current official information says.");
        }

        private async void AskSentinelNextStepsButton_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAskSentinelFollowUpAsync(
                "Explain the safest next steps, if any, for this question. Distinguish general guidance from actions verified on this computer, and do not claim any change has been performed unless Sentinel verified it.");
        }

        private async Task SubmitAskSentinelFollowUpAsync(string instruction)
        {
            if (_askSentinelBusy) return;

            string current = AskSentinelQuestionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                AskSentinelStatusText.Text = "Ask Sentinel a question first, then choose a follow-up.";
                return;
            }

            string original = ExtractAskSentinelOriginalQuestion(current);
            AskSentinelQuestionBox.Text = instruction + AskSentinelOriginalQuestionMarker + LimitFollowUpQuestion(original);
            await SubmitAskSentinelQuestionAsync();
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
    }
}
