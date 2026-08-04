using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        private readonly ListView _itemsList = new();
        private readonly TextBlock _emptyText = new();
        private readonly TextBlock _summaryText = new();
        private readonly Button _restoreButton = new();
        private readonly Button _deleteButton = new();
        private readonly ProgressRing _progress = new();
        private readonly TextBlock _statusText = new();
        private IReadOnlyList<QuarantineCatalogService.QuarantineCatalogEntry> _entries = Array.Empty<QuarantineCatalogService.QuarantineCatalogEntry>();

        public QuarantineManagerWindow()
        {
            Title = "Sentinel AI — Quarantine";
            Content = BuildContent();
            Activated += QuarantineManagerWindow_Activated;
        }

        private UIElement BuildContent()
        {
            Grid root = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 24, 39)) };
            ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel page = new() { Margin = new Thickness(32), Spacing = 18, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Center };

            TextBlock title = new() { Text = "Quarantine", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White) };
            TextBlock intro = new() { Text = "Files Sentinel isolated for your protection appear here. You can restore a verified item or permanently delete it after approval.", FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 209, 213, 219)) };

            Border listCard = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 41, 55)), CornerRadius = new CornerRadius(14), Padding = new Thickness(22) };
            StackPanel listPanel = new() { Spacing = 12 };
            listPanel.Children.Add(new TextBlock { Text = "Quarantined items", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White) });

            _emptyText.Text = "No quarantined items. Sentinel has nothing isolated right now.";
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
            detailPanel.Children.Add(new TextBlock { Text = "Investigation summary", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White) });
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
            page.Children.Add(listCard);
            page.Children.Add(detailCard);
            scroll.Content = page;
            root.Children.Add(scroll);
            return root;
        }

        private async void QuarantineManagerWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            Activated -= QuarantineManagerWindow_Activated;
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            SetBusy(true, "Refreshing verified quarantine records…");
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
                _summaryText.Text = hasItems ? "Select a quarantined item to review what Sentinel verified." : "No action is required. Sentinel has no quarantined files at this time.";
                _restoreButton.IsEnabled = false;
                _deleteButton.IsEnabled = false;
                _statusText.Text = "Sentinel will verify each action before reporting success.";
            }
            finally
            {
                SetBusy(false, _statusText.Text);
            }
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
            _summaryText.Text =
                $"File: {entry.FileName}\n" +
                $"Original location: {entry.OriginalPath}\n" +
                $"Quarantined: {entry.QuarantinedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}\n" +
                $"Verification: isolated copy present and catalog record matched\n" +
                $"SHA-256: {shortenedHash}\n\n" +
                "Restore returns the verified file to its original location. Delete Permanently removes the isolated copy and cannot be undone.";
            _restoreButton.IsEnabled = true;
            _deleteButton.IsEnabled = true;
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_itemsList.SelectedItem is not QuarantineItemView selected) return;
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
