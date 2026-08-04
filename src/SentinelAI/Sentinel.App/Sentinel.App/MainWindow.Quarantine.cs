using Microsoft.UI.Xaml;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private QuarantineManagerWindow? _quarantineManagerWindow;

        private void OpenQuarantineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_quarantineManagerWindow is null)
            {
                _quarantineManagerWindow = new QuarantineManagerWindow();
                _quarantineManagerWindow.Closed += (_, _) => _quarantineManagerWindow = null;
            }

            _quarantineManagerWindow.Activate();
        }
    }
}
