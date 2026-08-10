// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Headless.XUnit;
using Foundry.Connect.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class ConnectWindowLifetimeTests
{
    [AvaloniaFact]
    public void ClosingBeforeContinuation_RequestsUserAbortedExit()
    {
        var context = new MainWindowViewModelTestContext();
        var window = new MainWindow(
            context.ViewModel,
            context.LifetimeService,
            NullLogger<MainWindow>.Instance);
        window.Show();

        window.Close();

        Assert.True(context.LifetimeService.IsExitRequested);
        Assert.Equal(FoundryConnectExitCode.UserAborted, context.LifetimeService.ExitCode);
        context.ViewModel.Dispose();
    }
}
