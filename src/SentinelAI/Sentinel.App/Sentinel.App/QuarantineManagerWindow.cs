using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Models;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed class QuarantineManagerWindow : Window
    {
        private readonly QuarantineCatalogService _catalogService = new();
        private readonly QuarantineService _quarantineService = new();
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder = new();
        private readonly StoreSubscriptionService _subscriptionService = new();
        private readonly ProtectionStatusSummaryService _protectionStatusSummaryService = new();
        private readonly Func<SystemSnapshot>? _snapshotProvider;
        private readonly TextBlock _protectionHeadlineText = new();
        private readonly TextBlock _protectionFlaggedText = new();
        private readonly TextBlock _protectionResponseText = new();
        private readonly TextBlock _protectionCriteriaText = new();
        private readonly TextBlock _protectionActionStateText = new();
        private readonly ListView _itemsList = new();
        private readonly TextBlock _emptyText = new();
        private readonly TextBlock _summaryText = new();
        private readonly Button _restoreButton = new();
        private readonly Button _deleteButton = new();
        private readonly ProgressRing _progress = new();
        private readonly TextBlock _statusText = new();
        private IReadOnlyList<QuarantineCatalogService.QuarantineCatalogEntry> _entries = Array.Empty<QuarantineCatalogService.QuarantineCatalogEntry>();

        public QuarantineManagerWindow(Func<SystemSnapshot>? snapshotProvider = null)
        {
            _snapshotProvider = snapshotProvider;
            Title = "Sentinel AI — Protection Center";
            Content = BuildContent();
            Activated += QuarantineManagerWindow_Activated;
        }

        private UIElement BuildContent()
        {
            Grid root = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 24, 39)) };
            ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel page = new() { Margin = new Thickness(32), Spacing = 18, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Center };

            TextBlock title = new() { Text = "Protection Center", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)) };
            TextBlock intro = new() { Text = "See what Sentinel is currently watching, why it has or has not taken action, and manage files that are actually isolated in Sentinel's protected file quarantine.", FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 209, 213, 219)) };

            Border protectionCard = BuildProtectionCard();

            Border listCard = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 41, 55)), CornerRadius = new CornerRadius(14), Padding = new Thickness(22) };
            StackPanel listPanel = new() { Spacing = 12 };
            listPanel.Children.Add(new TextBlock { Text = "File Quarantine", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)) });
            listPanel.Children.Add(new TextBlock { Text = "This list contains only files Sentinel actually moved into its protected quarantine store after a verified file-quarantine action and approval. Network conditions use firewall containment and do not appear as files here.", FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)) });

            _emptyText.Text = "No quarantined files. Sentinel has not isolated a file into its protected quarantine store.";
            _emptyText.FontSize = 15;
            _emptyText.TextWrapping = TextWrapping.Wrap;
            _emptyText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175));

            _itemsList.SelectionMode = ListViewSelectionMode.Single;
            _itemsList.SelectionChanged += ItemsList_SelectionChanged;
            _itemsList.MinHeight = 160;
            listPanel.Children.Add(_emptyText);
            listPanel.Children.Add(_itemsList);
            listCard.Child = listPanel;

            Border detailCard = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 41, 55)), CornerRadius = new CornerRadius(14), Padding = new Thickness(22) };
            StackPanel detailPanel = new() { Spacing = 12 };
            detailPanel.Children.Add(new TextBlock { Text = "Investigation summary", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)) });
            _summaryText.Text = "Select a quarantined item to review what Sentinel verified.";
            _summaryText.FontSize = 15;
            _summaryText.TextWrapping = TextWrapping.Wrap;
            _summaryText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 209, 213, 219));
            detailPanel.Children.Add(_summaryText);

            StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
            _restoreButton.Content = "Restore";
            _restoreButton.IsEnabled = false;
            _restoreButton.Click += RestoreButton_Click;
            _deleteButton.Content = "Delete Permanently";
            _deleteButton.IsEnabled = false;
            _deleteButton.Click += DeleteButton_Click;
            actions.Children.Add(_restoreButton);
            actions.Children.Add(_deleteButton);
            detailPanel.Children.Add(actions);

            StackPanel progressRow = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
            _progress.Width = 20;
            _progress.Height = 20;
            _progress.IsActive = false;
            _progress.Visibility = Visibility.Collapsed;
            _statusText.Text = "Sentinel will verify each action before reporting success.";
            _statusText.FontSize = 14;
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175));
            progressRow.Children.Add(_progress);
            progressRow.Children.Add(_statusText);
            detailPanel.Children.Add(progressRow);
            detailCard.Child = detailPanel;

            page.Children.Add(title);
            page.Children.Add(intro);
            page.Children.Add(protectionCard);
            page.Children.Add(listCard);
            page.Children.Add(detailCard);
            scroll.Content = page;
            root.Children.Add(scroll);
            return root;
        }

        private async void QuarantineManagerWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            RefreshProtectionSummary();
            SetBusy(true, "Refreshing verified file-quarantine records…");
            try
            {
                _entries = await _catalogService.ReconcileAsync();
                _itemsList.Items.Clear();
                foreach (var entry in _entries.Where(item => item.IsPresent))
                {
                    _itemsList.Items.Add(new QuarantineItemView(entry));
                }

                bool hasItems = _itemsList.Items.Count > 0;
                _emptyText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
                _itemsList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                _summaryText.Text = hasItems
                    ? "Select a quarantined file to review what Sentinel verified."
                    : "There are no quarantined files to restore or delete. Flagged network/process/service conditions are shown above and remain separate unless Sentinel has an exact file target that is actually quarantined.";
                _restoreButton.IsEnabled = false;
                _deleteButton.IsEnabled = false;
                _statusText.Text = "Sentinel will verify each action before reporting success.";
            }
            finally
            {
                SetBusy(false, _statusText.Text);
            }
        }

        private Border BuildProtectionCard()
        {
            Border card = new()
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 41, 55)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22)
            };
            StackPanel panel = new() { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = "Current protection activity",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            });

            ConfigureProtectionText(_protectionHeadlineText, 16, true);
            ConfigureProtectionText(_protectionFlaggedText, 14, false);
            ConfigureProtectionText(_protectionResponseText, 14, false);
            ConfigureProtectionText(_protectionCriteriaText, 14, false);
            ConfigureProtectionText(_protectionActionStateText, 14, true);

            panel.Children.Add(_protectionHeadlineText);
            panel.Children.Add(new TextBlock { Text = "FLAGGED CONDITIONS", FontSize = 11, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 111, 136, 168)) });
            panel.Children.Add(_protectionFlaggedText);
            panel.Children.Add(new TextBlock { Text = "WHAT SENTINEL IS DOING", FontSize = 11, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 111, 136, 168)) });
            panel.Children.Add(_protectionResponseText);
            panel.Children.Add(new TextBlock { Text = "WHEN SENTINEL WILL ACT", FontSize = 11, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 111, 136, 168)) });
            panel.Children.Add(_protectionCriteriaText);
            panel.Children.Add(_protectionActionStateText);
            card.Child = panel;
            return card;
        }

        private static void ConfigureProtectionText(TextBlock text, double size, bool emphasized)
        {
            text.FontSize = size;
            text.TextWrapping = TextWrapping.Wrap;
            text.FontWeight = emphasized ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            text.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                emphasized
                    ? Windows.UI.Color.FromArgb(255, 203, 231, 255)
                    : Windows.UI.Color.FromArgb(255, 209, 213, 219));
        }

        private void RefreshProtectionSummary()
        {
            SystemSnapshot? snapshot = _snapshotProvider?.Invoke();
            if (snapshot is null)
            {
                _protectionHeadlineText.Text = "Protection status is available on the main dashboard.";
                _protectionFlaggedText.Text = "No live snapshot was provided to this window.";
                _protectionResponseText.Text = "Sentinel continues monitoring in the main application.";
                _protectionCriteriaText.Text = "Security-changing actions require verified evidence and a supported remediation path.";
                _protectionActionStateText.Text = "No action state is available in this window.";
                return;
            }

            ProtectionStatusSummaryService.ProtectionStatusSummary status = _protectionStatusSummaryService.Create(snapshot);
            _protectionHeadlineText.Text = status.Headline;
            _protectionFlaggedText.Text = status.FlaggedConditions;
            _protectionResponseText.Text = status.CurrentResponse;
            _protectionCriteriaText.Text = status.ActionCriteria;
            _protectionActionStateText.Text = status.ActionState;
        }

        private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_itemsList.SelectedItem is not QuarantineItemView selected)
            {
                _restoreButton.IsEnabled = false;
                _deleteButton.IsEnabled = false;
                return;
            }

            QuarantineCatalogService.QuarantineCatalogEntry entry = selected.Entry;
            string shortenedHash = entry.Sha256.Length > 16 ? entry.Sha256[..16] + "…" : entry.Sha256;
            string reason = string.IsNullOrWhiteSpace(entry.ReasonCode)
                ? "Sentinel verified the file was isolated, but the original investigation reason was recorded by an earlier build."
                : entry.ReasonCode;
            string confidence = entry.EvidenceConfidencePercent > 0
                ? $"{entry.EvidenceConfidencePercent}%"
                : "Not retained by the earlier record";

            _summaryText.Text =
                $"File: {entry.FileName}\n" +
                $"Original location: {entry.OriginalPath}\n" +
                $"Quarantined: {entry.QuarantinedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}\n\n" +
                $"Why Sentinel isolated it\n{reason}\n" +
                $"Evidence confidence: {confidence}\n\n" +
                "Verification\nThe isolated copy is present and its quarantine catalog record is intact.\n" +
                $"SHA-256: {shortenedHash}\n\n" +
                "Restore returns the verified file to its original location. Delete Permanently removes the isolated copy and cannot be undone.";
            _restoreButton.IsEnabled = true;
            _deleteButton.IsEnabled = true;
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_itemsList.SelectedItem is not QuarantineItemView selected) return;
            if (!await EnsurePremiumActionAsync("restore a quarantined file")) return;
            ContentDialog dialog = new()
            {
                Title = "Restore quarantined file?",
                Content = $"Sentinel will restore {selected.Entry.FileName} to its original location only if the quarantined copy still matches its verified record.",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            SetBusy(true, "Restoring and verifying the file…");
            QuarantineService.QuarantineResult result = await _quarantineService.RestoreAsync(_catalogService.ToRecord(selected.Entry), true);
            _outcomeRecorder.Record(result, "Restore quarantined file");
            if (result.Succeeded && result.Verified) await _catalogService.RemoveAsync(selected.Entry.QuarantinePath);
            _statusText.Text = result.Message;
            await ShowResultAsync(result.Succeeded && result.Verified ? "File restored" : "Restore not completed", result.Message);
            await RefreshAsync();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_itemsList.SelectedItem is not QuarantineItemView selected) return;
            if (!await EnsurePremiumActionAsync("permanently delete a quarantined file")) return;
            ContentDialog dialog = new()
            {
                Title = "Delete quarantined file permanently?",
                Content = $"This permanently deletes {selected.Entry.FileName} from Sentinel quarantine. This cannot be undone. Sentinel will verify the isolated file before deletion and verify that it is gone afterward.",
                PrimaryButtonText = "Delete Permanently",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            SetBusy(true, "Deleting and verifying the quarantined file…");
            QuarantineService.QuarantineResult result = await _quarantineService.DeletePermanentlyAsync(_catalogService.ToRecord(selected.Entry), true);
            _outcomeRecorder.Record(result, "Delete quarantined file permanently");
            if (result.Succeeded && result.Verified) await _catalogService.RemoveAsync(selected.Entry.QuarantinePath);
            _statusText.Text = result.Message;
            await ShowResultAsync(result.Succeeded && result.Verified ? "File permanently deleted" : "Delete not completed", result.Message);
            await RefreshAsync();
        }

        private async Task<bool> EnsurePremiumActionAsync(string action)
        {
            SubscriptionState subscription = await _subscriptionService.GetStateAsync();
            if (subscription.IsActive) return true;

            await ShowResultAsync(
                "Subscription required",
                $"Free local threat monitoring remains active. An active Sentinel AI subscription is required to {action}.");
            return false;
        }

        private async Task ShowResultAsync(string title, string message)
        {
            ContentDialog dialog = new() { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = ((FrameworkElement)Content).XamlRoot };
            await dialog.ShowAsync();
        }

        private void SetBusy(bool busy, string message)
        {
            _progress.IsActive = busy;
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _restoreButton.IsEnabled = !busy && _itemsList.SelectedItem is QuarantineItemView;
            _deleteButton.IsEnabled = !busy && _itemsList.SelectedItem is QuarantineItemView;
            _statusText.Text = message;
        }

        private sealed class QuarantineItemView
        {
            public QuarantineItemView(QuarantineCatalogService.QuarantineCatalogEntry entry) => Entry = entry;
            public QuarantineCatalogService.QuarantineCatalogEntry Entry { get; }
            public override string ToString() => $"{Entry.FileName}  •  {Entry.QuarantinedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}";
        }
    }
}
