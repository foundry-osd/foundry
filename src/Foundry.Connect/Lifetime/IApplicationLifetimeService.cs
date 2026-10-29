// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// The single exit authority of Foundry.Connect: every exit path (success, user close, failure) requests its exit code
/// here, and only the first request decides the process exit code returned to Foundry.Bootstrap.
/// </summary>
public interface IApplicationLifetimeService
{
    /// <summary>
    /// Requests the application exit with <paramref name="exitCode"/>. The first request wins and shuts the application
    /// down; later requests are ignored.
    /// </summary>
    /// <param name="exitCode">The process exit code to return to Foundry.Bootstrap.</param>
    /// <returns><see langword="true"/> when this request decided the exit code; <see langword="false"/> when an exit was already requested.</returns>
    bool TryExit(ChildRuntimeExitCode exitCode);

    /// <summary>
    /// Gets the exit code of the winning exit request, or <see langword="null"/> while no exit was requested.
    /// </summary>
    ChildRuntimeExitCode? RequestedExitCode { get; }

    /// <summary>
    /// Gets a token that is canceled when the first exit request is accepted, before the application shutdown starts.
    /// </summary>
    CancellationToken ExitRequested { get; }
}
