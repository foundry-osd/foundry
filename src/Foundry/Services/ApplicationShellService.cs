using System.Windows;

namespace Foundry.Services;

public sealed class ApplicationShellService : IApplicationShellService
{
    public void Shutdown()
    {
        Application.Current.Shutdown();
    }

    public void ShowAbout()
    {
        MessageBox.Show(
            "Foundry\nWPF .NET 10\nMVVM bootstrap ready.",
            "About Foundry",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
