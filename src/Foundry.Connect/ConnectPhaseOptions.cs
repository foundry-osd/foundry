// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect;

/// <summary>Hooks the Foundry.WinPE UI host gives the Foundry Connect phase.</summary>
public sealed class ConnectPhaseOptions
{
    /// <summary>
    /// Gets the readiness hook: called once, on the UI thread, when Connect's first window renders, which is its main window
    /// or the deployment password prompt shown before it. It is not called when Connect fails before any window rendered.
    /// </summary>
    public required Action OnFirstWindowRendered { get; init; }
}
