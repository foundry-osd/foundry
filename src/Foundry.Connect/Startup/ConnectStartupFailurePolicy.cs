// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Startup;

/// <summary>
/// Chooses the process exit code Foundry.Connect returns to Foundry.WinPE when a stage fails.
/// </summary>
public static class ConnectStartupFailurePolicy
{
    /// <summary>
    /// Maps the stage that failed to its exit code.
    /// </summary>
    /// <param name="stage">The stage that was running when the failure happened.</param>
    /// <returns>The exit code that tells Foundry.WinPE which kind of failure stopped Foundry.Connect.</returns>
    public static ChildRuntimeExitCode Map(ConnectStartupStage stage) => stage switch
    {
        ConnectStartupStage.Configuration => ChildRuntimeExitCode.ConfigurationFailure,
        ConnectStartupStage.Services or ConnectStartupStage.UiStartup => ChildRuntimeExitCode.StartupFailure,
        ConnectStartupStage.Running => ChildRuntimeExitCode.RuntimeFailure,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown Foundry.Connect startup stage.")
    };

    /// <summary>
    /// Maps a failure to its exit code: once the UI reported readiness to Foundry.WinPE, every failure is a runtime failure.
    /// </summary>
    /// <param name="stage">The stage that was running when the failure happened.</param>
    /// <param name="isUiReady">Whether the UI already reported readiness; the deployment password prompt can report it before the page.</param>
    /// <returns>The exit code that tells Foundry.WinPE which kind of failure stopped Foundry.Connect.</returns>
    public static ChildRuntimeExitCode MapFailure(ConnectStartupStage stage, bool isUiReady)
        => Map(isUiReady ? ConnectStartupStage.Running : stage);

    /// <summary>
    /// Chooses the code the Connect phase returns when it ends with an exception. An accepted user abort or failure keeps its
    /// code, so a failure raised while that exit is in progress cannot replace it; an accepted success never does, because
    /// the phase did not complete it: the host must not start Foundry Deploy after it.
    /// </summary>
    /// <param name="requestedExitCode">The exit code accepted by the exit authority, or <see langword="null"/> when none was requested.</param>
    /// <param name="failureExitCode">The mapped failure code (see <see cref="MapFailure"/>).</param>
    /// <returns>The phase exit code; never <see cref="ChildRuntimeExitCode.Success"/>.</returns>
    public static int ResolveFailureExitCode(ChildRuntimeExitCode? requestedExitCode, ChildRuntimeExitCode failureExitCode)
        => requestedExitCode is { } requested && requested != ChildRuntimeExitCode.Success ? (int)requested : (int)failureExitCode;
}
