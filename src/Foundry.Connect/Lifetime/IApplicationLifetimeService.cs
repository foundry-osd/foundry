// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// The single exit authority of the Foundry Connect phase: every exit path (success, user close, failure) requests its exit
/// code here, and only the first request decides the exit code the phase returns to the Foundry.WinPE UI host.
/// </summary>
/// <remarks>
/// Connect's <see cref="Foundry.Wpf.Shell.IApplicationShellService.Shutdown"/> requests <see cref="ChildRuntimeExitCode.UserAborted"/>
/// here, so it can never be read as a successful network step.
/// </remarks>
public interface IApplicationLifetimeService
{
    /// <summary>
    /// Requests the end of the Connect phase with <paramref name="exitCode"/>. The first request wins and completes the
    /// phase; later requests are ignored.
    /// </summary>
    /// <param name="exitCode">The exit code the phase returns to the Foundry.WinPE UI host.</param>
    /// <returns><see langword="true"/> when this request decided the exit code; <see langword="false"/> when an exit was already requested.</returns>
    bool TryExit(ChildRuntimeExitCode exitCode);

    /// <summary>
    /// Gets the exit code of the winning exit request, or <see langword="null"/> while no exit was requested.
    /// </summary>
    ChildRuntimeExitCode? RequestedExitCode { get; }

    /// <summary>
    /// Gets a token that is canceled when the first exit request is accepted, before the phase completes.
    /// </summary>
    CancellationToken ExitRequested { get; }
}
