using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics;

namespace Sentinel.App
{
    public sealed partial class OptionsWindow : Window
    {
        private readonly WindowsStartupRegistrationService _startupService = new();
        private readonly OptimizationSettingsService _optimizationSettingsService = new();
        private readonly StoreSubscriptionService _subscriptionService = new();
        private bool _loading;
        private bool _initialLayoutApplied;

        public OptionsWindow()
        {
            InitializeComponent();
            Activated += OptionsWindow_Activated;
        }

        private async void OptionsWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_initialLayoutApplied)
            {
                _initialLayoutApplied = true;
                AppWindow.Resize(new SizeInt32(850, 820));

                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.PreferredMinimumWidth = 850;
                    presenter.PreferredMinimumHeight = 700;
                }
            }

            Activated -= OptionsWindow_Activated;
            LoadStartupState();
            LoadOptimizationState();
            await LoadSubscriptionStateAsync();
        }

        private async Task LoadSubscriptionStateAsync()
        {
            MonthlySubscriptionButton.IsEnabled = false;
            AnnualSubscriptionButton.IsEnabled = false;
            SubscriptionPlanText.Text = "Checking Microsoft Store subscription…";
            SubscriptionStatusText.Text = "Sentinel is verifying your subscription.";

            SubscriptionState state = await _subscriptionService.GetStateAsync();
            SubscriptionPlanText.Text = state.DisplayName;
            SubscriptionStatusText.Text = state.Summary;

            if (state.Plan == SubscriptionPlan.Development)
            {
                MonthlySubscriptionButton.Visibility = Visibility.Collapsed;
                AnnualSubscriptionButton.Visibility = Visibility.Collapsed;
                return;
            }

            MonthlySubscriptionButton.Visibility = Visibility.Visible;
            AnnualSubscriptionButton.Visibility = Visibility.Visible;

            if (state.MonthlyProduct is not null)
            {
                MonthlySubscriptionButton.Content = $"Monthly — {state.MonthlyProduct.Price.FormattedPrice}";
                MonthlySubscriptionButton.IsEnabled = !state.IsActive;
            }
            else
            {
                MonthlySubscriptionButton.Content = "Monthly — coming soon";
                MonthlySubscriptionButton.IsEnabled = false;
            }

            if (state.AnnualProduct is not null)
            {
                AnnualSubscriptionButton.Content = $"Annual — {state.AnnualProduct.Price.FormattedPrice} — best value";
                AnnualSubscriptionButton.IsEnabled = !state.IsActive;
            }
            else
            {
                AnnualSubscriptionButton.Content = "Annual — coming soon";
                AnnualSubscriptionButton.IsEnabled = false;
            }
        }

        private async void MonthlySubscriptionButton_Click(object sender, RoutedEventArgs e) =>
            await PurchaseSubscriptionAsync(SubscriptionPlan.Monthly);

        private async void AnnualSubscriptionButton_Click(object sender, RoutedEventArgs e) =>
            await PurchaseSubscriptionAsync(SubscriptionPlan.Annual);

        private async void RefreshSubscriptionButton_Click(object sender, RoutedEventArgs e) =>
            await LoadSubscriptionStateAsync();

        private async Task PurchaseSubscriptionAsync(SubscriptionPlan plan)
        {
            MonthlySubscriptionButton.IsEnabled = false;
            AnnualSubscriptionButton.IsEnabled = false;
            SubscriptionStatusText.Text = "Opening Microsoft Store secure checkout…";

            SubscriptionPurchaseResult result = await _subscriptionService.PurchaseAsync(plan);
            SubscriptionStatusText.Text = result.Message;
            if (result.Succeeded)
                await LoadSubscriptionStateAsync();
            else
            {
                MonthlySubscriptionButton.IsEnabled = true;
                AnnualSubscriptionButton.IsEnabled = true;
            }
        }

        private void LoadStartupState()
        {
            _loading = true;

            if (!HasInstalledPackageIdentity())
            {
                StartWithWindowsToggle.IsOn = false;
                StartWithWindowsToggle.IsEnabled = false;
                StartupStatusText.Text = "This option is available in the installed Sentinel app. Visual Studio development runs do not have the package identity Windows needs for startup registration.";
                _loading = false;
                return;
            }

            StartWithWindowsToggle.IsEnabled = true;
            bool preferred = _startupService.GetUserStartupPreference();
            bool registered = _startupService.IsStartupRegistered();
            StartWithWindowsToggle.IsOn = preferred && registered;
            StartupStatusText.Text = preferred
                ? registered
                    ? "Startup is enabled and verified."
                    : "Startup is enabled but Windows registration needs repair. Turn the option off and back on to repair it."
                : "Startup is disabled by your choice.";
            _loading = false;
        }

        private void LoadOptimizationState()
        {
            _loading = true;
            OptimizationSettings settings = _optimizationSettingsService.Load();

            AutomaticOptimizationToggle.IsOn = settings.AutomaticOptimizationEnabled;
            VerifyEveryChangeCheckBox.IsChecked = settings.VerifyEveryChange;
            RollbackCheckBox.IsChecked = settings.RollBackWhenPossible;

            for (int i = 0; i < OptimizationModeComboBox.Items.Count; i++)
            {
                if (OptimizationModeComboBox.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), settings.Mode.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    OptimizationModeComboBox.SelectedIndex = i;
                    break;
                }
            }

            if (OptimizationModeComboBox.SelectedIndex < 0)
                OptimizationModeComboBox.SelectedIndex = 0;

            OptimizationStatusText.Text = settings.AutomaticOptimizationEnabled
                ? $"Automatic optimization is enabled in {settings.Mode} mode. Sentinel will act only when current evidence supports a safe optimization."
                : "Automatic optimization is off. Sentinel will continue monitoring performance without making optimization changes.";

            _loading = false;
        }

        private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading || !StartWithWindowsToggle.IsEnabled)
                return;

            WindowsStartupRegistrationService.StartupRegistrationResult result =
                _startupService.SetStartupEnabled(StartWithWindowsToggle.IsOn);

            StartupStatusText.Text = result.Summary;

            _loading = true;
            StartWithWindowsToggle.IsOn = _startupService.GetUserStartupPreference() &&
                                          _startupService.IsStartupRegistered();
            _loading = false;
        }

        private void OptimizationSetting_Changed(object sender, RoutedEventArgs e)
        {
            SaveOptimizationState();
        }

        private void OptimizationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveOptimizationState();
        }

        private void SaveOptimizationState()
        {
            if (_loading)
                return;

            OptimizationMode mode = OptimizationMode.Conservative;
            if (OptimizationModeComboBox.SelectedItem is ComboBoxItem selectedItem &&
                Enum.TryParse(selectedItem.Tag?.ToString(), ignoreCase: true, out OptimizationMode parsedMode))
            {
                mode = parsedMode;
            }

            OptimizationSettings settings = new(
                AutomaticOptimizationToggle.IsOn,
                mode,
                VerifyEveryChangeCheckBox.IsChecked == true,
                RollbackCheckBox.IsChecked == true);

            bool saved = _optimizationSettingsService.Save(settings);
            OptimizationStatusText.Text = !saved
                ? "Sentinel could not save the optimization preference. No optimization setting was changed."
                : settings.AutomaticOptimizationEnabled
                    ? $"Automatic optimization is enabled in {settings.Mode} mode. Sentinel will verify evidence before making changes."
                    : "Automatic optimization is off. Sentinel will continue monitoring performance without making optimization changes.";
        }

        private static bool HasInstalledPackageIdentity()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(Package.Current.Id.FamilyName);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
