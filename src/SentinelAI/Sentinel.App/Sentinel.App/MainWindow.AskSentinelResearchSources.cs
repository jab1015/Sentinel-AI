using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private const int MaximumDisplayedResearchSources = 8;
        private StackPanel? _askSentinelResearchSourcesPanel;

        private void RenderAskSentinelAnswer(
            string answer,
            IReadOnlyList<CloudAiCitation>? citations = null,
            IReadOnlyList<CloudAiSource>? sources = null)
        {
            string text = answer ?? string.Empty;
            AskSentinelAnswerText.Inlines.Clear();
            AskSentinelAnswerText.Text = string.Empty;

            IReadOnlyList<CloudAiCitation> safeCitations = NormalizeDisplayCitations(text, citations);
            if (safeCitations.Count == 0)
            {
                AskSentinelAnswerText.Text = text;
            }
            else
            {
                int cursor = 0;
                foreach (CloudAiCitation citation in safeCitations)
                {
                    if (citation.StartIndex > cursor)
                    {
                        AskSentinelAnswerText.Inlines.Add(new Run
                        {
                            Text = text[cursor..citation.StartIndex]
                        });
                    }

                    Hyperlink link = new() { NavigateUri = new Uri(citation.Url) };
                    link.Inlines.Add(new Run
                    {
                        Text = text[citation.StartIndex..citation.EndIndex]
                    });
                    AskSentinelAnswerText.Inlines.Add(link);
                    cursor = citation.EndIndex;
                }

                if (cursor < text.Length)
                {
                    AskSentinelAnswerText.Inlines.Add(new Run
                    {
                        Text = text[cursor..]
                    });
                }
            }

            ShowAskSentinelResearchSources(sources, safeCitations);
        }

        private void ClearAskSentinelResearchSources()
        {
            if (_askSentinelResearchSourcesPanel is not null)
            {
                _askSentinelResearchSourcesPanel.Children.Clear();
                _askSentinelResearchSourcesPanel.Visibility = Visibility.Collapsed;
            }
            AskSentinelAnswerText.Inlines.Clear();
        }

        private void ShowAskSentinelResearchSources(
            IReadOnlyList<CloudAiSource>? sources,
            IReadOnlyList<CloudAiCitation>? citations)
        {
            List<CloudAiSource> merged = new();
            if (sources is not null)
            {
                foreach (CloudAiSource source in sources)
                    AddDisplaySource(merged, source.Title, source.Url);
            }

            if (citations is not null)
            {
                foreach (CloudAiCitation citation in citations)
                    AddDisplaySource(merged, citation.Title, citation.Url);
            }

            if (merged.Count == 0)
            {
                if (_askSentinelResearchSourcesPanel is not null)
                {
                    _askSentinelResearchSourcesPanel.Children.Clear();
                    _askSentinelResearchSourcesPanel.Visibility = Visibility.Collapsed;
                }
                return;
            }

            EnsureAskSentinelResearchSourcesPanel();
            if (_askSentinelResearchSourcesPanel is null) return;

            _askSentinelResearchSourcesPanel.Children.Clear();
            _askSentinelResearchSourcesPanel.Children.Add(new TextBlock
            {
                Text = "Sources",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 142, 165, 193))
            });

            foreach (CloudAiSource source in merged.Take(MaximumDisplayedResearchSources))
            {
                if (!TryCreateSafeResearchUri(source.Url, out Uri? uri) || uri is null) continue;
                string host = uri.Host;
                string title = string.IsNullOrWhiteSpace(source.Title) ? host : source.Title.Trim();
                string label = title.Equals(host, StringComparison.OrdinalIgnoreCase)
                    ? host
                    : $"{title} — {host}";

                HyperlinkButton button = new()
                {
                    Content = label,
                    NavigateUri = uri,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(0, 2, 0, 2),
                    FontSize = 11
                };
                _askSentinelResearchSourcesPanel.Children.Add(button);
            }

            _askSentinelResearchSourcesPanel.Children.Add(new TextBlock
            {
                Text = "External sources provide guidance; Sentinel still verifies claims about this computer from local evidence.",
                FontSize = 10,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 117, 138, 166)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            _askSentinelResearchSourcesPanel.Visibility = Visibility.Visible;
        }

        private void EnsureAskSentinelResearchSourcesPanel()
        {
            if (_askSentinelResearchSourcesPanel is not null ||
                AskSentinelAnswerBorder.Child is not StackPanel answerStack)
                return;

            _askSentinelResearchSourcesPanel = new StackPanel
            {
                Spacing = 3,
                Margin = new Thickness(0, 4, 0, 2),
                Visibility = Visibility.Collapsed
            };

            int answerIndex = answerStack.Children.IndexOf(AskSentinelAnswerText);
            int insertionIndex = answerIndex >= 0
                ? Math.Min(answerIndex + 1, answerStack.Children.Count)
                : answerStack.Children.Count;
            answerStack.Children.Insert(insertionIndex, _askSentinelResearchSourcesPanel);
        }

        private static IReadOnlyList<CloudAiCitation> NormalizeDisplayCitations(
            string answer,
            IReadOnlyList<CloudAiCitation>? citations)
        {
            if (string.IsNullOrEmpty(answer) || citations is null || citations.Count == 0)
                return Array.Empty<CloudAiCitation>();

            List<CloudAiCitation> accepted = new();
            int lastEnd = 0;
            foreach (CloudAiCitation citation in citations
                         .OrderBy(value => value.StartIndex)
                         .ThenBy(value => value.EndIndex))
            {
                if (accepted.Count >= MaximumDisplayedResearchSources) break;
                if (citation.StartIndex < lastEnd ||
                    citation.StartIndex < 0 ||
                    citation.EndIndex <= citation.StartIndex ||
                    citation.EndIndex > answer.Length ||
                    !TryCreateSafeResearchUri(citation.Url, out _))
                    continue;

                accepted.Add(citation);
                lastEnd = citation.EndIndex;
            }
            return accepted;
        }

        private static IReadOnlyList<CloudAiSource> MergeAskSentinelResearchSources(
            ExternalInvestigationResult external,
            IReadOnlyList<CloudAiSource>? aiSources)
        {
            List<CloudAiSource> result = new();
            foreach (ExternalSourceEvidence source in external.Sources)
                AddDisplaySource(result, source.SourceName, source.Uri);

            if (aiSources is not null)
            {
                foreach (CloudAiSource source in aiSources)
                    AddDisplaySource(result, source.Title, source.Url);
            }

            return result.Take(MaximumDisplayedResearchSources).ToArray();
        }

        private static void AddDisplaySource(List<CloudAiSource> result, string? title, string? url)
        {
            if (result.Count >= MaximumDisplayedResearchSources ||
                !TryCreateSafeResearchUri(url, out Uri? uri) || uri is null ||
                result.Any(source => source.Url.Equals(uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
                return;

            string normalizedTitle = string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim();
            if (normalizedTitle.Length > 300) normalizedTitle = normalizedTitle[..300];
            result.Add(new CloudAiSource(normalizedTitle, uri.AbsoluteUri));
        }

        private static bool TryCreateSafeResearchUri(string? value, out Uri? uri)
        {
            uri = null;
            string candidate = (value ?? string.Empty).Trim();
            if (candidate.Length == 0 || candidate.Length > 2_048 ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed) ||
                !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(parsed.Host) ||
                !string.IsNullOrWhiteSpace(parsed.UserInfo))
                return false;

            uri = parsed;
            return true;
        }
    }
}
