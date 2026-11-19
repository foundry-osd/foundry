// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Foundry.Wpf.Shell;

namespace Foundry.Wpf.Tests.Shell;

public sealed class ShellContentAttachmentTests
{
    [Fact]
    public void Rendered_CompletesOnlyWhenTheShellReportsThisContent() => RunOnSta(() =>
    {
        var shell = new FakeShellHost();
        var content = new Border();
        using var attachment = new ShellContentAttachment(shell, content);

        shell.RaiseRendered(new Border());
        Assert.False(attachment.Rendered.IsCompleted);

        shell.RaiseRendered(content);
        Assert.True(attachment.Rendered.IsCompletedSuccessfully);
    });

    [Fact]
    public void ShellWindowClosed_EndsTheAttachmentAndCallsBackOnce() => RunOnSta(() =>
    {
        var shell = new FakeShellHost();
        int closed = 0;
        using var attachment = new ShellContentAttachment(shell, new Border(), () => closed++);

        shell.Window.Close();

        Assert.Equal(1, closed);
        Assert.True(attachment.IsEnded);
        Assert.True(attachment.Rendered.IsCanceled);
        Assert.Equal(0, shell.RenderedHandlerCount);
    });

    [Fact]
    public void Dispose_StopsReactingToTheShellsEvents() => RunOnSta(() =>
    {
        var shell = new FakeShellHost();
        var content = new Border();
        int closed = 0;
        var attachment = new ShellContentAttachment(shell, content, () => closed++);

        attachment.Dispose();
        attachment.Dispose();
        shell.RaiseRendered(content);
        shell.Window.Close();

        Assert.True(attachment.IsEnded);
        Assert.True(attachment.Rendered.IsCanceled);
        Assert.Equal(0, closed);
        Assert.Equal(0, shell.RenderedHandlerCount);
    });

    // A window and its events need an STA thread; no application or rendering is needed.
    private static void RunOnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private sealed class FakeShellHost : IShellHost
    {
        private EventHandler<ShellContentRenderedEventArgs>? _rendered;

        public Window Window { get; } = new();

        public int RenderedHandlerCount => _rendered?.GetInvocationList().Length ?? 0;

        public event EventHandler<CancelEventArgs>? CloseRequested
        {
            add { }
            remove { }
        }

        public event EventHandler<ShellContentRenderedEventArgs>? ShellContentRendered
        {
            add => _rendered += value;
            remove => _rendered -= value;
        }

        public void ShowContent(FrameworkElement content) => throw new NotSupportedException();

        public void RaiseRendered(FrameworkElement content) => _rendered?.Invoke(this, new ShellContentRenderedEventArgs(content));
    }
}
