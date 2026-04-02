using System.Windows;
using Foundry.Connect.ViewModels;
using Foundry.Connect.Views;

namespace Foundry.Connect.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    public void ShowAbout()
    {
        var viewModel = new AboutDialogViewModel(
            FoundryConnectApplicationInfo.AboutTitle,
            FoundryConnectApplicationInfo.AppName,
            FoundryConnectApplicationInfo.Version,
            FoundryConnectApplicationInfo.DescriptionLine1,
            FoundryConnectApplicationInfo.DescriptionLine2,
            FoundryConnectApplicationInfo.Footer);
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
