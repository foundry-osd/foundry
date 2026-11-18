// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Wpf.Shell;

/// <summary>
/// Ties one page to the shell that shows it: <see cref="Rendered"/> completes when the shell reports that this page rendered
/// (other pages of the same shell are ignored), and the attachment ends when the shell window closes or when it is disposed.
/// Ending removes every handler it added to the shell, so the shell no longer keeps the page alive.
/// </summary>
/// <remarks>Created and used on the UI thread.</remarks>
public sealed class ShellContentAttachment : IDisposable
{
    private readonly IShellHost _shell;
    private readonly FrameworkElement _content;
    private readonly Action? _onShellClosed;
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Subscribes to the shell's render event and to the close of its window.</summary>
    /// <param name="shell">The shell that shows <paramref name="content"/>.</param>
    /// <param name="content">The page this attachment follows.</param>
    /// <param name="onShellClosed">
    /// Called once when the shell window closes while the attachment is active, after it ended; not called after
    /// <see cref="Dispose"/>.
    /// </param>
    public ShellContentAttachment(IShellHost shell, FrameworkElement content, Action? onShellClosed = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(content);
        _shell = shell;
        _content = content;
        _onShellClosed = onShellClosed;
        shell.ShellContentRendered += OnShellContentRendered;
        shell.Window.Closed += OnShellClosed;
    }

    /// <summary>
    /// Gets a task that completes when the shell reported this page rendered, or is cancelled when the attachment ended first.
    /// Its continuations never run inline on the reporting thread.
    /// </summary>
    public Task Rendered => _rendered.Task;

    /// <summary>Gets whether the shell window closed or the attachment was disposed.</summary>
    public bool IsEnded { get; private set; }

    /// <summary>Ends the attachment: removes its handlers from the shell and cancels a pending <see cref="Rendered"/>.</summary>
    public void Dispose() => End();

    private void OnShellContentRendered(object? sender, ShellContentRenderedEventArgs e)
    {
        if (ReferenceEquals(e.Content, _content))
        {
            _rendered.TrySetResult();
        }
    }

    private void OnShellClosed(object? sender, EventArgs e)
    {
        if (End())
        {
            _onShellClosed?.Invoke();
        }
    }

    private bool End()
    {
        if (IsEnded)
        {
            return false;
        }

        IsEnded = true;
        _shell.ShellContentRendered -= OnShellContentRendered;
        _shell.Window.Closed -= OnShellClosed;
        _rendered.TrySetCanceled();
        return true;
    }
}
