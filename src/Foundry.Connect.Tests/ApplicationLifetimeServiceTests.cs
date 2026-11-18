// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Connect.Lifetime;
using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Tests;

public sealed class ApplicationLifetimeServiceTests
{
    [Fact]
    public async Task TryExit_FirstRequestCompletesThePhaseOnce()
    {
        var service = new ApplicationLifetimeService();
        Assert.False(service.Completion.IsCompleted);

        Assert.True(service.TryExit(ChildRuntimeExitCode.UserAborted));
        Assert.False(service.TryExit(ChildRuntimeExitCode.Success));

        Assert.Equal(ChildRuntimeExitCode.UserAborted, await service.Completion.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ChildRuntimeExitCode.UserAborted, service.RequestedExitCode);
        Assert.True(service.ExitRequested.IsCancellationRequested);
    }

    [Fact]
    public void TryExit_NeverShutsTheApplicationDown()
    {
        // The test process has no WPF application: an exit that called Application.Shutdown would need one.
        Assert.Null(Application.Current);
        var service = new ApplicationLifetimeService();

        Assert.True(service.TryExit(ChildRuntimeExitCode.Success));

        Assert.True(service.Completion.IsCompletedSuccessfully);
        Assert.Null(Application.Current);
    }
}
