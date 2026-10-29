// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Startup;

/// <summary>
/// Chooses the process exit code Foundry.Connect returns to Foundry.Bootstrap when a stage fails.
/// </summary>
public static class ConnectStartupFailurePolicy
{
    /// <summary>
    /// Maps the stage that failed to its exit code.
    /// </summary>
    /// <param name="stage">The stage that was running when the failure happened.</param>
    /// <returns>The exit code that tells Foundry.Bootstrap which kind of failure stopped Foundry.Connect.</returns>
    public static ChildRuntimeExitCode Map(ConnectStartupStage stage) => stage switch
    {
        ConnectStartupStage.Configuration => ChildRuntimeExitCode.ConfigurationFailure,
        ConnectStartupStage.Services or ConnectStartupStage.UiStartup => ChildRuntimeExitCode.StartupFailure,
        ConnectStartupStage.Running => ChildRuntimeExitCode.RuntimeFailure,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown Foundry.Connect startup stage.")
    };

    /// <summary>
    /// Chooses the code the process returns: an exit request accepted by the exit authority always wins, so a failure
    /// raised while that exit is in progress cannot replace it.
    /// </summary>
    /// <param name="requestedExitCode">The exit code accepted by the exit authority, or <see langword="null"/> when none was requested.</param>
    /// <param name="fallbackExitCode">The code to return when no exit was requested: the WPF exit code or the mapped failure code.</param>
    /// <returns>The process exit code.</returns>
    public static int ResolveProcessExitCode(ChildRuntimeExitCode? requestedExitCode, int fallbackExitCode)
        => requestedExitCode is { } requested ? (int)requested : fallbackExitCode;
}
