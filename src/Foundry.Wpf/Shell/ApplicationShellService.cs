// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Wpf.Shell;

public sealed class ApplicationShellService : IApplicationShellService
{
    private readonly ILocalizationService _localizationService;
    private readonly WpfApplicationIdentity _identity;

    public ApplicationShellService(ILocalizationService localizationService, WpfApplicationIdentity identity)
    {
        _localizationService = localizationService;
        _identity = identity;
    }

    public void ShowAbout()
    {
        var viewModel = new AboutDialogViewModel(
            _localizationService.Strings["About.Title"],
            _localizationService.Strings["App.Name"],
            _identity.Version,
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

    public void Shutdown()
    {
        Application.Current?.Shutdown();
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
