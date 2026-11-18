// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Wpf.Shell;
using Xunit;

namespace Foundry.Deploy.Tests;

[Collection(nameof(WpfApplicationHostCollection))]
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

    [Fact]
    public Task PresentContent_MakesTheShellWindowTheMainWindowAndShowsTheContentInIt() => WpfApplicationTestHost.RunAsync(() =>
    {
        Application application = Application.Current;
        Window? previous = application.MainWindow;
        // Another main window first: the application adopts a new window as its main window only when it has none.
        var other = new Window();
        application.MainWindow = other;
        var window = new Window();
        var content = new System.Windows.Controls.UserControl();
        var shell = new FakeShellHost(window);
        try
        {
            DeployPhase.PresentContent(application, shell, content);

            Assert.Same(window, application.MainWindow);
            Assert.Same(content, shell.ShownContent);
            Assert.Equal(1, shell.ShowCount);
        }
        finally
        {
            application.MainWindow = previous;
            window.Close();
            other.Close();
        }

        return Task.CompletedTask;
    });

    private sealed class FakeShellHost(Window? window = null) : IShellHost
    {
        public FrameworkElement? ShownContent { get; private set; }

        public int ShowCount { get; private set; }

        public Window Window => window ?? throw new NotSupportedException("Deploy must not touch the window while handling a close request.");

        public event EventHandler<CancelEventArgs>? CloseRequested;

        public event EventHandler<ShellContentRenderedEventArgs>? ShellContentRendered
        {
            add { }
            remove { }
        }

        public void ShowContent(FrameworkElement content)
        {
            ShownContent = content;
            ShowCount++;
        }

        public void RaiseCloseRequested(CancelEventArgs args) => CloseRequested?.Invoke(this, args);
    }
}
