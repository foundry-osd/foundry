// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Wpf.Shell;

/// <summary>The data of <see cref="IShellHost.ShellContentRendered"/>: the content that rendered.</summary>
/// <param name="content">The content shown by <see cref="IShellHost.ShowContent"/> that has rendered.</param>
public sealed class ShellContentRenderedEventArgs(FrameworkElement content) : EventArgs
{
    /// <summary>Gets the content that rendered.</summary>
    public FrameworkElement Content { get; } = content;
}
