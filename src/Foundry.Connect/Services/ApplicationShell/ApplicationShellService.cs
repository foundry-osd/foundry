using System.Windows;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.ViewModels;
using Foundry.Connect.Views;

namespace Foundry.Connect.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    private readonly ILocalizationService _localizationService;

    public ApplicationShellService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public void ShowAbout()
    {
        var viewModel = new AboutDialogViewModel(
            _localizationService.Strings["About.Title"],
            _localizationService.Strings["App.Name"],
            FoundryConnectApplicationInfo.Version,
            _localizationService.Strings["About.DescriptionLine1"],
            _localizationService.Strings["About.DescriptionLine2"],
            _localizationService.Strings["About.Footer"]);
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
