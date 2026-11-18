// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Shell;

namespace Foundry.Connect;

/// <summary>Hooks the Foundry.WinPE UI host gives the Foundry Connect phase.</summary>
public sealed class ConnectPhaseOptions
{
    /// <summary>
    /// Gets the shell that shows Connect's content and owns the window: the dialog owner, the page transition and the close
    /// policy. Connect shows its content in it, handles its close requests while the phase runs, and keeps the content shown
    /// (disabled) after a successful end.
    /// </summary>
    public required IShellHost Shell { get; init; }

    /// <summary>
    /// Gets the readiness hook: called once, on the UI thread, when Connect's first content renders, which is its page
    /// or the deployment password prompt shown before it. It is not called when Connect fails before any content rendered.
    /// </summary>
    public required Action OnFirstContentRendered { get; init; }

    /// <summary>
    /// Gets the hook called once, on the UI thread, when Connect loaded its configuration and built its services, before its
    /// content is created, or <see langword="null"/>. It is not called when the configuration cannot be loaded.
    /// </summary>
    public Action? OnConfigurationLoaded { get; init; }
}
