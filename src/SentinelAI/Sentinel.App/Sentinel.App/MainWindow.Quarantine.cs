using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private QuarantineManagerWindow? _quarantineManagerWindow;

        private void OpenQuarantineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_quarantineManagerWindow is null)
            {
                _quarantineManagerWindow = new QuarantineManagerWindow(
                    () => _engine.CurrentSnapshot,
                    () => _lastVerifiedNetworkContainmentTarget,
                    OpenAskSentinelFromProtectionCenterAsync);
                _quarantineManagerWindow.Closed += (_, _) => _quarantineManagerWindow = null;
            }

            _quarantineManagerWindow.Activate();
        }

        private async Task OpenAskSentinelFromProtectionCenterAsync(string question)
        {
            _quarantineManagerWindow?.AppWindow.Hide();
            AppWindow.Show();
            Activate();
            AskSentinelQuestionBox.Text = question;
            await SubmitAskSentinelQuestionAsync();
        }
    }
}
