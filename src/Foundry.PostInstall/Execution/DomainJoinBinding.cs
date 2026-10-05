// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;

namespace Foundry.PostInstall.Execution;

/// <summary>Validates the sole join/verification pair and its exclusive sensitive credential ownership before authentication.</summary>
internal sealed record DomainJoinBinding(PreOobeExecutionAction JoinAction, DomainJoinActionParameters Parameters,
    PreOobeExecutionAction VerificationAction)
{
    public static DomainJoinBinding Validate(PreOobeExecutionPlan plan)
    {
        if (!Guid.TryParseExact(plan.OperationId, "N", out _) || !Guid.TryParseExact(plan.AttemptId, "N", out _))
            throw new InvalidDataException("Domain operation identity is invalid.");
        var joins = plan.Actions.Where(a => a.BuiltInKind == PreOobeBuiltInKind.DomainJoinAndPlacement).ToArray();
        var checks = plan.Actions.Where(a => a.BuiltInKind == PreOobeBuiltInKind.VerifyDomainMembership).ToArray();
        if (joins.Length != 1 || checks.Length != 1 || Array.IndexOf(plan.Actions.ToArray(), joins[0]) >= Array.IndexOf(plan.Actions.ToArray(), checks[0]))
            throw new InvalidDataException("Domain action pairing is invalid.");
        int joinIndex = Array.IndexOf(plan.Actions.ToArray(), joins[0]);
        if (plan.Actions[joinIndex + 1] != checks[0] || plan.Actions.Skip(joinIndex + 1).Any(action =>
            action.BuiltInKind is PreOobeBuiltInKind.Driver or PreOobeBuiltInKind.Network))
            throw new InvalidDataException("Domain action order is invalid.");
        var parameters = joins[0].Parameters?.Deserialize<DomainJoinActionParameters>(ExecutionJournal.JsonOptions)
            ?? throw new InvalidDataException("Domain parameters are missing.");
        var verification = checks[0].Parameters?.Deserialize<DomainMembershipVerificationParameters>(ExecutionJournal.JsonOptions)
            ?? throw new InvalidDataException("Domain verification is missing.");
        string expected = $"Payloads/DomainJoin/{plan.OperationId}/credentials.bin";
        if (!DomainJoinCredentialContext.IsValidDomainName(parameters.DomainName) || !ComputerNameRules.IsValid(parameters.ComputerName) ||
            parameters.TargetOuDn is not null && !DistinguishedNameRules.IsWithinDomain(parameters.TargetOuDn, parameters.DomainName) ||
            parameters.CredentialPayloadPath != expected || verification.JoinActionId != joins[0].Id ||
            verification.DomainName != parameters.DomainName || verification.ComputerName != parameters.ComputerName)
            throw new InvalidDataException("Domain parameters are invalid.");
        var payloads = plan.OwnedPayloads.Where(p => p.RelativePath.Replace('\\', '/').Equals(expected, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (payloads.Length != 1 || payloads[0].RelativePath != expected || !payloads[0].IsSensitive || payloads[0].IsDirectory ||
            payloads[0].ConsumerActionIds.Count != 1 || payloads[0].ConsumerActionIds[0] != joins[0].Id ||
            plan.OwnedPayloads.Any(p => p != payloads[0] && (p.ConsumerActionIds.Contains(joins[0].Id) || p.ConsumerActionIds.Contains(checks[0].Id) ||
                expected.StartsWith(p.RelativePath.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Domain credential ownership is invalid.");
        return new(joins[0], parameters, checks[0]);
    }

    public static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Plan hash is invalid.");
    }

    public static void ValidateBoot(string boot)
    {
        if (string.IsNullOrWhiteSpace(boot) || boot.Length > 128 || boot.Any(char.IsControl))
            throw new InvalidDataException("Boot identity is invalid.");
    }

    public static void RequireRunning(string root, PreOobeExecutionPlan plan, string hash, string boot, string actionId)
    {
        ValidateHash(hash);
        ValidateBoot(boot);
        PreOobePlanValidator.ValidatePlan(plan);
        var binding = Validate(plan);
        JournalState state = new ExecutionJournal(root).Read();
        PreOobePlanValidator.ValidateState(plan, state, hash);
        if (state.Status != "Running" || state.BootIdentity != boot || state.Cursor >= plan.Actions.Count || state.Substep != 0 ||
            plan.Actions[state.Cursor].Id != actionId || state.Actions.GetValueOrDefault(actionId)?.Status != "Running" ||
            state.UnsafePayloadBootIdentity == boot && !(binding.VerificationAction.Id == actionId &&
                state.UnsafeActionId == binding.JoinAction.Id))
            throw new InvalidDataException("The worker is not the active action.");
    }

    public void RequireAction(PreOobeExecutionAction action, bool verification = false)
    {
        var expected = verification ? VerificationAction : JoinAction;
        if (action.Id != expected.Id || action.BuiltInKind != expected.BuiltInKind || action.CustomAction is not null ||
            action.Parameters?.GetRawText() != expected.Parameters?.GetRawText()) throw new InvalidDataException("Domain action identity changed.");
    }
}
