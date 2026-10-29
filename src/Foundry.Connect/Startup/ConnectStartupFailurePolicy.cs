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
}
