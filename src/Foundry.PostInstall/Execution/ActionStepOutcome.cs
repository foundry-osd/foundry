// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;

namespace Foundry.PostInstall.Execution;

public sealed record ActionStepOutcome(bool Succeeded, int? ExitCode = null, string? FailureCode = null,
    bool RestartRequested = false, bool TerminationUncertain = false, int? NextSubstep = null,
    bool HasWarnings = false);

public interface IPreOobeActionExecutor
{
    Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken);
}

public sealed record OrchestrationOutcome(string Status, int ExitCode);
