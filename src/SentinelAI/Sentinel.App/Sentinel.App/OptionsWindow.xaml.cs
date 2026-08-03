using Microsoft.UI.Xaml;
using Sentinel.App.Services;

namespace Sentinel.App
{
    public sealed partial class OptionsWindow : Window
    {
        private readonly WindowsStartupRegistrationService _startupService = new();
        private bool _loading;

        public OptionsWindow()
        {
            InitializeComponent();
            Activated += OptionsWindow_Activated;
        }

        private void OptionsWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            Activated -= OptionsWindow_Activated;
            LoadStartupState();
        }

        private void LoadStartupState()
        {
            _loading = true;
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
            if (_loading)
                return;

            WindowsStartupRegistrationService.StartupRegistrationResult result =
                _startupService.SetStartupEnabled(StartWithWindowsToggle.IsOn);

            StartupStatusText.Text = result.Summary;

            _loading = true;
            StartWithWindowsToggle.IsOn = _startupService.GetUserStartupPreference() &&
                                          _startupService.IsStartupRegistered();
            _loading = false;
        }
    }
}
