// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Wpf.Shell;

namespace Foundry.Deploy.Tests;

public sealed class DeployShellCloseTests
{
    [Fact]
    public void CloseRequest_WhileADeploymentRuns_IsCancelledAndStartsTheCancellation()
    {
        var shell = new FakeShellHost();
        int cancellations = 0;
        int ended = 0;
        using IDisposable attachment = DeployPhase.AttachCloseHandler(shell, () => true, () => cancellations++, () => ended++);

        var args = new CancelEventArgs();
        shell.RaiseCloseRequested(args);

        Assert.True(args.Cancel);
        Assert.Equal(1, cancellations);
        Assert.Equal(0, ended);
    }

    [Fact]
    public void CloseRequest_WithoutADeployment_IsAcceptedAndEndsThePhase()
    {
        var shell = new FakeShellHost();
        int cancellations = 0;
        int ended = 0;
        using IDisposable attachment = DeployPhase.AttachCloseHandler(shell, () => false, () => cancellations++, () => ended++);

        var args = new CancelEventArgs();
        shell.RaiseCloseRequested(args);

        Assert.False(args.Cancel);
        Assert.Equal(0, cancellations);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void DisposingTheAttachment_DetachesTheCloseHandler()
    {
        var shell = new FakeShellHost();
        int ended = 0;
        DeployPhase.AttachCloseHandler(shell, () => false, () => { }, () => ended++).Dispose();

        shell.RaiseCloseRequested(new CancelEventArgs());

        Assert.Equal(0, ended);
    }

    [Fact]
    public void RunOnce_CallsTheActionOnlyTheFirstTime()
    {
        int calls = 0;
        Action once = DeployPhase.RunOnce(() => calls++);

        once();
        once();

        Assert.Equal(1, calls);
    }

    private sealed class FakeShellHost : IShellHost
    {
        public Window Window => throw new NotSupportedException("Deploy must not touch the window while handling a close request.");

        public event EventHandler<CancelEventArgs>? CloseRequested;

        public event EventHandler? FirstContentRendered
        {
            add { }
            remove { }
        }

        public void ShowContent(FrameworkElement content) => throw new NotSupportedException();

        public void RaiseCloseRequested(CancelEventArgs args) => CloseRequested?.Invoke(this, args);
    }
}
