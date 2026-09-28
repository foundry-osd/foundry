// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;

namespace Foundry.PostInstall.Execution;

/// <summary>A display snapshot of journaled progress; reporting must never change execution state.</summary>
public sealed record PostInstallActionProgress(string Id, string Name, string Status, TimeSpan? Elapsed = null, int? ExitCode = null);

/// <summary>Restores completed actions after restart and exposes countdowns without persisting presentation state.</summary>
public sealed record PostInstallProgress(IReadOnlyList<PostInstallActionProgress> Actions, string Status,
    bool IsResuming = false, int? RestartSecondsRemaining = null)
{
    /// <summary>Uses English built-in labels while preserving user-authored action names.</summary>
    public static string GetActionName(PreOobeExecutionAction action) => action.BuiltInKind switch
    {
        PreOobeBuiltInKind.Driver => "Install drivers",
        PreOobeBuiltInKind.Network => "Configure network",
        PreOobeBuiltInKind.Appx => "Remove AppX packages",
        PreOobeBuiltInKind.AiRemoval => "Remove AI components",
        PreOobeBuiltInKind.Activation => "Activate Windows",
        PreOobeBuiltInKind.Cleanup => "Cleanup",
        _ => action.Name
    };
}
