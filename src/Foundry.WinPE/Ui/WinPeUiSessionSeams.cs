// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect;
using Foundry.Deploy;

namespace Foundry.WinPE.Ui;

/// <summary>Runs the Foundry Connect phase for <see cref="WinPeUiSession"/>.</summary>
internal interface IConnectPhaseRunner
{
    /// <summary>Runs the phase on the UI thread, in the session's shell, and completes when it ends.</summary>
    /// <param name="options">The shared shell and the session hooks.</param>
    /// <param name="cancellationToken">Ends a running phase as a user abort.</param>
    /// <returns>The phase result; on success its content stays shown, disabled, in the shell.</returns>
    Task<ConnectPhaseResult> RunAsync(ConnectPhaseOptions options, CancellationToken cancellationToken);
}

/// <summary>Prepares the clock and time zone between the phases.</summary>
internal interface ISystemPreparationStep
{
    /// <summary>Runs the preparation off the UI thread; a failure is the caller's to tolerate.</summary>
    /// <returns>
    /// Whether the system clock is usable afterwards: <see langword="true"/> when it was set or verified against an internet
    /// time source (or was already usable), <see langword="false"/> when the sync failed or the process is not in Windows PE.
    /// </returns>
    Task<bool> RunAsync(CancellationToken cancellationToken);
}

/// <summary>Runs the Foundry Deploy phase for <see cref="WinPeUiSession"/>.</summary>
internal interface IDeployPhaseRunner
{
    /// <summary>Runs the phase on the UI thread, in the session's shell, and returns its exit code.</summary>
    /// <param name="options">The shared shell, the Connect hand-over and the session hooks.</param>
    /// <param name="cancellationToken">Asks the phase to end.</param>
    Task<int> RunAsync(DeployPhaseOptions options, CancellationToken cancellationToken);
}
