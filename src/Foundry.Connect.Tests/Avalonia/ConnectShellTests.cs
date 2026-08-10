// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Foundry.Avalonia.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class ConnectShellTests
{
    [AvaloniaFact]
    public void MainWindow_UsesChromelessMaximizedFoundryShellWithoutFooter()
    {
        var context = new MainWindowViewModelTestContext();
        var window = new MainWindow(
            context.ViewModel,
            context.LifetimeService,
            NullLogger<MainWindow>.Instance);

        window.Show();

        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.NotNull(window.FindControl<FoundryShell>("Shell"));
        Assert.Null(window.FindControl<Control>("Footer"));
        Assert.NotEmpty(window.FindControl<MenuItem>("LanguageMenu")!.Items);
        Assert.NotNull(window.FindControl<MenuItem>("ToolsMenu"));
        Assert.NotNull(window.FindControl<MenuItem>("DiagnosticsMenuItem"));
        Assert.NotNull(window.FindControl<MenuItem>("HelpMenu"));
        window.Close();
        context.ViewModel.Dispose();
    }
}
