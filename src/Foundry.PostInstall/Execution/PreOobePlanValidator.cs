// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;

namespace Foundry.PostInstall.Execution;

/// <summary>Shares structural and journal identity validation between the parent runner and its workers.</summary>
internal static class PreOobePlanValidator
{
    public static void ValidatePlan(PreOobeExecutionPlan plan)
    {
        if (plan.SchemaVersion != 1 || plan.RuntimeContractVersion is not (1 or 2) ||
            string.IsNullOrWhiteSpace(plan.OperationId) || string.IsNullOrWhiteSpace(plan.AttemptId) ||
            plan.Actions.Count > PreOobeConfigurationValidator.MaximumActions + Enum.GetValues<PreOobeBuiltInKind>().Length ||
            plan.Actions.Count(action => action.CustomAction is not null) > PreOobeConfigurationValidator.MaximumActions ||
            plan.Actions.Where(action => action.BuiltInKind.HasValue).Select(action => action.BuiltInKind).Distinct().Count() != plan.Actions.Count(action => action.BuiltInKind.HasValue) ||
            plan.Actions.Select(action => action.Id).Distinct(StringComparer.Ordinal).Count() != plan.Actions.Count)
            throw new InvalidDataException("The execution plan is invalid.");
        if (plan.Actions.Any(action => action.BuiltInKind is PreOobeBuiltInKind.DomainJoinAndPlacement or PreOobeBuiltInKind.VerifyDomainMembership))
            DomainJoinBinding.Validate(plan);
        bool sawCustom = false;
        bool sawCleanup = false;
        foreach (PreOobeExecutionAction action in plan.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id) || action.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')) ||
                (action.CustomAction is null) == (action.BuiltInKind is null) ||
                action.BuiltInKind is { } kind && !Enum.IsDefined(kind) || sawCleanup ||
                sawCustom && action.BuiltInKind is not (null or PreOobeBuiltInKind.Cleanup))
                throw new InvalidDataException("The execution action is invalid.");
            sawCustom |= action.CustomAction is not null;
            sawCleanup |= action.BuiltInKind == PreOobeBuiltInKind.Cleanup;
            if (action.CustomAction is { } custom &&
                (custom.RestartDelaySeconds is < 0 or > PreOobeConfigurationValidator.MaximumRestartDelaySeconds ||
                 custom.Kind != PreOobeActionKind.Restart && custom.RestartDelaySeconds != 0))
                throw new InvalidDataException("The restart delay is invalid.");
        }
        foreach (PreOobeOwnedPayload payload in plan.OwnedPayloads)
            if (!(payload.RelativePath.Replace('/', '\\').StartsWith("Payloads\\", StringComparison.OrdinalIgnoreCase) ||
                  payload.RelativePath.Replace('/', '\\').Equals("Work\\PreOobe\\" + plan.OperationId, StringComparison.OrdinalIgnoreCase)) ||
                payload.ConsumerActionIds.Any(id => !plan.Actions.Any(action => action.Id == id)))
                throw new InvalidDataException("Payload ownership is invalid.");
    }

    public static void ValidateState(PreOobeExecutionPlan plan, JournalState state, string planHash)
    {
        if (state.SchemaVersion != 1 || state.OperationId != plan.OperationId || state.AttemptId != plan.AttemptId ||
            !string.Equals(state.PlanHash, planHash, StringComparison.OrdinalIgnoreCase) || state.Cursor < 0 ||
            state.Cursor > plan.Actions.Count || state.Generation < 0 || state.Substep < 0 || state.Substep > 10000 ||
            state.RestartCount < 0 || state.RestartCount > 1024 ||
            state.Status == "AwaitingRestart" && (state.RestartCount == 0 || string.IsNullOrWhiteSpace(state.BootIdentity)) ||
            state.Status is not ("Pending" or "Running" or "AwaitingRestart" or "Completing" or "Succeeded" or "CompletedWithErrors" or "Failed" or "Interrupted") ||
            state.Status == "Completing" && state.CompletionStatus is not ("Succeeded" or "CompletedWithErrors" or "Failed" or "Interrupted") ||
            state.Status == "Pending" && (state.Cursor != 0 || state.Substep != 0 || state.Actions.Count != 0 || state.RestartCount != 0))
            throw new InvalidDataException("The journal does not match this operation.");
    }

}
