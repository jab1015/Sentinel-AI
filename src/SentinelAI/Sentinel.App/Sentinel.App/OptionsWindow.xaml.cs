using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Sentinel.App.Services;
using System;
using Windows.ApplicationModel;
using Windows.Graphics;

namespace Sentinel.App
{
    public sealed partial class OptionsWindow : Window
    {
        private readonly WindowsStartupRegistrationService _startupService = new();
        private bool _loading;
        private bool _initialLayoutApplied;

        public OptionsWindow()
        {
            InitializeComponent();
            Activated += OptionsWindow_Activated;
        }

        private void OptionsWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_initialLayoutApplied)
            {
                _initialLayoutApplied = true;
                AppWindow.Resize(new SizeInt32(850, 700));

                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.PreferredMinimumWidth = 850;
                    presenter.PreferredMinimumHeight = 700;
                }
            }

            Activated -= OptionsWindow_Activated;
            LoadStartupState();
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
