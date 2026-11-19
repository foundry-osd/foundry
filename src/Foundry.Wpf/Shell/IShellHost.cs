// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;

namespace Foundry.Wpf.Shell;

/// <summary>
/// The single window of a WPF process that shows its pages one after the other. A page is a plain
/// <see cref="FrameworkElement"/>; the host owns the window chrome, the page transition and the close policy.
/// </summary>
public interface IShellHost
{
    /// <summary>Gets the host window, for dialog owners and window-level state.</summary>
    Window Window { get; }

    /// <summary>Shows the content with a transition (none for the first content); the previous content is removed.</summary>
    /// <param name="content">The page to show.</param>
    void ShowContent(FrameworkElement content);

    /// <summary>
    /// Raised on the UI thread when the operator closes the window; a handler sets <see cref="CancelEventArgs.Cancel"/> to keep
    /// it open. Without a handler the close is refused.
    /// </summary>
    event EventHandler<CancelEventArgs> CloseRequested;

    /// <summary>
    /// Raised on the UI thread once for each <see cref="ShowContent"/> call, after that content first rendered, with the content
    /// in <see cref="ShellContentRenderedEventArgs.Content"/>; never again for the same call. It is not raised for a content a
    /// later <see cref="ShowContent"/> replaced before it rendered. A page reacts only to its own content (see
    /// <see cref="ShellContentAttachment"/>).
    /// </summary>
    event EventHandler<ShellContentRenderedEventArgs> ShellContentRendered;
}
