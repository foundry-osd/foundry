// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Input;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Diagnostics;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy;

public partial class MainWindow : Window
{
    private readonly TroubleshootingConsoleSettings _troubleshootingConsole;

    public MainWindow(MainWindowViewModel viewModel, IDeployConfigurationService configurationService)
    {
        _troubleshootingConsole = configurationService.LoadOptional().Document?.TroubleshootingConsole
            ?? new TroubleshootingConsoleSettings();

        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // PreviewKeyDown is used so the shortcut still works while a text box has focus.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (TroubleshootingConsole.IsShortcut(
                _troubleshootingConsole,
                key.ToString(),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            TroubleshootingConsole.TryLaunch();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
