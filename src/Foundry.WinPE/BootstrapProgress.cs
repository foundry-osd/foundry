// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.WinPE;

/// <summary>
/// Ordered boot stages, reported as <c>last_stage</c>: the guardian's environment preparation, then the phases of the UI
/// process (Foundry Connect, system preparation, Foundry Deploy), read from its UI state file.
/// </summary>
internal enum BootstrapStage { Environment, Connect, System, Deploy }

/// <summary>Textual status remains meaningful without terminal colors or cursor support.</summary>
internal enum BootstrapStatus { Pending, Running, Completed, Warning, Failed, Cancelled }

/// <summary>Describes progress without forwarding diagnostic exception details to the console.</summary>
internal sealed record BootstrapProgress(BootstrapStage Stage, BootstrapStatus Status, string Message);

/// <summary>Distinguishes operator cancellation from an unrecoverable boot failure.</summary>
internal enum BootstrapOutcome { Succeeded, Cancelled, Failed }

/// <summary>
/// Terminal result retains the failing stage and child code for diagnostics. <see cref="PayloadVersion"/> is the file
/// version of the Foundry.WinPE executable once the UI process was launched, or <see langword="null"/> before.
/// </summary>
internal sealed record BootstrapResult(BootstrapOutcome Outcome, BootstrapStage Stage, int? ChildExitCode = null,
    string? FailureCategory = null, string? ChildStartupStage = null, string? PayloadVersion = null)
{
    internal int ExitCode => Outcome switch
    {
        BootstrapOutcome.Succeeded => 0,
        BootstrapOutcome.Cancelled => (int)ChildRuntimeExitCode.UserAborted,
        _ => 1
    };
}
