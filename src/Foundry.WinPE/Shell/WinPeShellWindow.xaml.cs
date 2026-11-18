// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Foundry.Wpf.Motion;
using Foundry.Wpf.Shell;

namespace Foundry.WinPE.Shell;

/// <summary>
/// The maximized, borderless window of the WinPE UI process. It shows the Connect and Deploy pages one after the other and
/// leaves the close decision to its owner through <see cref="CloseRequested"/>.
/// </summary>
/// <remarks>
/// <para>
/// The host creates it hidden; the first <see cref="ShowContent"/> shows it, so no empty window appears while a phase builds
/// its services. It uses only the standalone application resources (Fluent and the Foundry.Wpf styles): the Connect and
/// Deploy styles are not merged in every process that creates it.
/// </para>
/// <para>
/// A close while Windows shuts down or restarts is always accepted, like a programmatic close with <see cref="AllowClose"/>:
/// the window never holds up the end of the Windows session.
/// </para>
/// </remarks>
public partial class WinPeShellWindow : Window, IShellHost
{
    private const double SlideOffset = 24;

    private readonly Func<bool> _isSystemShuttingDown;
    private bool _hasContent;

    /// <summary>Initializes the window, hidden until its first content.</summary>
    public WinPeShellWindow()
        : this(SystemShutdown.IsInProgress)
    {
    }

    /// <summary>Initializes the window with the test seam that tells whether Windows is shutting down.</summary>
    /// <param name="isSystemShuttingDown">Returns whether Windows is shutting down or restarting.</param>
    internal WinPeShellWindow(Func<bool> isSystemShuttingDown)
    {
        _isSystemShuttingDown = isSystemShuttingDown;
        InitializeComponent();
    }

    /// <inheritdoc />
    Window IShellHost.Window => this;

    /// <inheritdoc />
    public event EventHandler<CancelEventArgs>? CloseRequested;

    /// <inheritdoc />
    public event EventHandler<ShellContentRenderedEventArgs>? ShellContentRendered;

    /// <summary>
    /// Gets or sets whether the window may close without asking <see cref="CloseRequested"/>. The host sets it when the
    /// process ends, so a programmatic close is never refused.
    /// </summary>
    internal bool AllowClose { get; set; }

    /// <inheritdoc />
    /// <remarks>The first content also shows the window when it is still hidden.</remarks>
    public void ShowContent(FrameworkElement content)
    {
        ArgumentNullException.ThrowIfNull(content);

        bool animate = _hasContent;
        _hasContent = true;
        ContentHost.Content = content;
        if (animate)
        {
            TransitionAnimator.FadeAndTranslateX(ContentHost, ContentTransform, SlideOffset);
        }

        RunAfterRender(() => OnContentRendered(content));
        if (!IsVisible)
        {
            Show();
        }
    }

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (AllowClose || _isSystemShuttingDown())
        {
            return;
        }

        EventHandler<CancelEventArgs>? handler = CloseRequested;
        if (handler is null)
        {
            e.Cancel = true;
            return;
        }

        handler(this, e);
    }

    private void OnContentRendered(FrameworkElement content)
    {
        // A later content may already have replaced this one.
        if (!ReferenceEquals(ContentHost.Content, content))
        {
            return;
        }

        // A page that chose its own focus target (Connect does in Loaded) keeps it.
        if (!ContainsFocus(content))
        {
            content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        ShellContentRendered?.Invoke(this, new ShellContentRenderedEventArgs(content));
    }

    private bool ContainsFocus(FrameworkElement content)
    {
        if (content.IsKeyboardFocusWithin)
        {
            return true;
        }

        return FocusManager.GetFocusedElement(this) is DependencyObject focused && (ReferenceEquals(focused, content) || content.IsAncestorOf(focused));
    }

    // Before the window is shown nothing has rendered yet: wait for its first render instead of the dispatcher queue.
    private void RunAfterRender(Action action)
    {
        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, action);
            return;
        }

        void Handler(object? sender, EventArgs args)
        {
            ContentRendered -= Handler;
            action();
        }

        ContentRendered += Handler;
    }
}
