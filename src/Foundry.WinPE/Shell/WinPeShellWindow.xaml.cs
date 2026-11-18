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
/// It uses only the standalone application resources (Fluent and the Foundry.Wpf styles): the Connect and Deploy styles are
/// not merged in every process that creates it.
/// </remarks>
public partial class WinPeShellWindow : Window, IShellHost
{
    private const double SlideOffset = 24;

    private bool _hasContent;
    private bool _firstContentRenderedRaised;

    /// <summary>Initializes the window.</summary>
    public WinPeShellWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    Window IShellHost.Window => this;

    /// <inheritdoc />
    public event EventHandler<CancelEventArgs>? CloseRequested;

    /// <inheritdoc />
    public event EventHandler? FirstContentRendered;

    /// <summary>
    /// Gets or sets whether the window may close without asking <see cref="CloseRequested"/>. The host sets it when the
    /// process ends, so a programmatic close is never refused.
    /// </summary>
    internal bool AllowClose { get; set; }

    /// <inheritdoc />
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
    }

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (AllowClose)
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

        content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        if (!_firstContentRenderedRaised)
        {
            _firstContentRenderedRaised = true;
            FirstContentRendered?.Invoke(this, EventArgs.Empty);
        }
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
