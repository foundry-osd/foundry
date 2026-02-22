using System.Windows;

namespace Foundry.Deploy.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    public bool ConfirmWarning(string title, string message)
    {
        MessageBoxResult result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
