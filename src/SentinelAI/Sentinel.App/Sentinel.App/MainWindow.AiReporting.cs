using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly AiContentReportService _aiContentReportService = new();

        private async void ReportAiResponseButton_Click(object sender, RoutedEventArgs e)
        {
            string responseText = AskSentinelAnswerText.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                await ShowAiReportMessageAsync("Nothing to report", "Ask Sentinel has not displayed a response yet.");
                return;
            }

            ComboBox categoryBox = new()
            {
                Header = "Reason",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };
            categoryBox.Items.Add(new ComboBoxItem { Content = "Inappropriate or offensive" });
            categoryBox.Items.Add(new ComboBoxItem { Content = "Unsafe or harmful" });
            categoryBox.Items.Add(new ComboBoxItem { Content = "Incorrect or misleading" });
            categoryBox.Items.Add(new ComboBoxItem { Content = "Other" });

            TextBox commentsBox = new()
            {
                Header = "Comments (optional)",
                PlaceholderText = "Tell us what was wrong with this response.",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 100,
                MaxLength = 1000
            };

            TextBlock privacyText = new()
            {
                Text = "This report includes the AI response, the reason you select, and your optional comments. Sentinel does not attach unrelated system diagnostics to this report.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.75
            };

            StackPanel panel = new() { Spacing = 14 };
            panel.Children.Add(new TextBlock
            {
                Text = "Report this response if you believe it is inappropriate, unsafe, inaccurate, misleading, or otherwise problematic.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(categoryBox);
            panel.Children.Add(commentsBox);
            panel.Children.Add(privacyText);

            ContentDialog dialog = new()
            {
                Title = "Report AI response",
                Content = panel,
                PrimaryButtonText = "Submit report",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };

            ContentDialogResult choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
                return;

            string category = (categoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other";
            ReportAiResponseButton.IsEnabled = false;
            ReportAiResponseButton.Content = "Submitting…";

            AiContentReportResult result;
            try
            {
                result = await _aiContentReportService.SubmitAsync(category, commentsBox.Text, responseText);
            }
            finally
            {
                ReportAiResponseButton.IsEnabled = true;
                ReportAiResponseButton.Content = "Report AI response";
            }

            await ShowAiReportMessageAsync(
                result.Succeeded ? "Report submitted" : "Report not submitted",
                result.Message);
        }

        private async Task ShowAiReportMessageAsync(string title, string message)
        {
            ContentDialog confirmation = new()
            {
                Title = title,
                Content = message,
                CloseButtonText = "Close",
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await confirmation.ShowAsync();
        }
    }
}
