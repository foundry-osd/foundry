// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Wpf.Shell;

/// <summary>
/// Default <see cref="IApplicationShellService"/> backed by the current WPF <see cref="Application"/>.
/// Dialogs are modal and parented to the active window, falling back to the main window.
/// </summary>
/// <remarks>
/// Members must be called on the UI thread. A phase hosted in a shared application (Foundry Connect and Foundry Deploy in the
/// Foundry.WinPE UI process) passes its own end action, so <see cref="Shutdown"/> ends only that phase, and sets
/// <see cref="Application.MainWindow"/> to the shell window it runs in, so the owner fallback targets that window.
/// </remarks>
public sealed class ApplicationShellService : IApplicationShellService
{
    private readonly ILocalizationService _localizationService;
    private readonly WpfApplicationIdentity _identity;
    private readonly Action? _shutdown;

    /// <summary>Initializes a shell whose <see cref="Shutdown"/> shuts the WPF application down.</summary>
    /// <param name="localizationService">Provides the About dialog texts in the active language.</param>
    /// <param name="identity">Supplies the application version shown in the About dialog.</param>
    public ApplicationShellService(ILocalizationService localizationService, WpfApplicationIdentity identity)
        : this(localizationService, identity, shutdown: null)
    {
    }

    /// <summary>Initializes a shell whose <see cref="Shutdown"/> runs <paramref name="shutdown"/> instead.</summary>
    /// <param name="localizationService">Provides the About dialog texts in the active language.</param>
    /// <param name="identity">Supplies the application version shown in the About dialog.</param>
    /// <param name="shutdown">Ends the hosting phase; <see langword="null"/> shuts the WPF application down.</param>
    public ApplicationShellService(ILocalizationService localizationService, WpfApplicationIdentity identity, Action? shutdown)
    {
        _localizationService = localizationService;
        _identity = identity;
        _shutdown = shutdown;
    }

    /// <inheritdoc/>
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

    /// <summary>Shows a Yes/No warning message box with No as the default answer.</summary>
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

    /// <inheritdoc/>
    public void Shutdown()
    {
        if (_shutdown is not null)
        {
            _shutdown();
            return;
        }

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
