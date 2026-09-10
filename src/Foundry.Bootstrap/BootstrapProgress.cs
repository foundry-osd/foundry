// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap;

/// <summary>Ordered user-visible stages; application launch is distinct from readiness.</summary>
internal enum BootstrapStage { Environment, Connect, System, DeploymentPreparation, Deploy }

/// <summary>Textual status remains meaningful without terminal colors or cursor support.</summary>
internal enum BootstrapStatus { Pending, Running, Completed, Warning, Failed, Cancelled }

/// <summary>Describes progress without forwarding diagnostic exception details to the console.</summary>
internal sealed record BootstrapProgress(BootstrapStage Stage, BootstrapStatus Status, string Message);

/// <summary>Distinguishes operator cancellation from an unrecoverable boot failure.</summary>
internal enum BootstrapOutcome { Succeeded, Cancelled, Failed }

/// <summary>Terminal result retains the failing stage and child code for diagnostics.</summary>
internal sealed record BootstrapResult(BootstrapOutcome Outcome, BootstrapStage Stage, int? ChildExitCode = null)
{
    internal int ExitCode => Outcome == BootstrapOutcome.Succeeded ? 0 : 1;
}
