// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Views;

public partial class ConnectDiagnosticsDialog : Window
{
    public ConnectDiagnosticsDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ConnectDiagnosticsDialog(ConnectDiagnosticsDialogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        Opened += OnOpened;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is ConnectDiagnosticsDialogViewModel viewModel)
        {
            viewModel.Initialization = viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
