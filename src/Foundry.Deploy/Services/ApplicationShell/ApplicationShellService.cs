using System.Windows;
using Foundry.Deploy.ViewModels;
using Foundry.Deploy.Views;

namespace Foundry.Deploy.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    public void ShowAbout()
    {
        var viewModel = new AboutDialogViewModel(
            FoundryDeployApplicationInfo.AboutTitle,
            FoundryDeployApplicationInfo.AppName,
            FoundryDeployApplicationInfo.Version,
            FoundryDeployApplicationInfo.DescriptionLine1,
            FoundryDeployApplicationInfo.DescriptionLine2,
            FoundryDeployApplicationInfo.Footer);
        var dialog = new AboutDialog
        {
            DataContext = viewModel,
            Owner = ResolveOwnerWindow()
        };
        EventHandler closeRequestedHandler = (_, _) => dialog.Close();

        try
        {
            viewModel.CloseRequested += closeRequestedHandler;
            _ = dialog.ShowDialog();
        }
        finally
        {
            viewModel.CloseRequested -= closeRequestedHandler;
        }
    }

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

    private static Window? ResolveOwnerWindow()
    {
        if (Application.Current?.Windows is null)
        {
            return Application.Current?.MainWindow;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return Application.Current.MainWindow;
    }
}
