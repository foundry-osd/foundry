// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
namespace Foundry.PostInstall.Execution;

/// <summary>Parent-owned password-free report. The worker never writes this file.</summary>
internal sealed class DomainJoinResultStore(string root, PreOobeExecutionPlan plan, string planHash)
{
    private readonly string path = OwnedPaths.Resolve(root, "State/PreOobe/domain-join-result.json");
    /// <summary>Creates a credential-free report seed with no assumed installed-Windows boot.</summary>
    public DomainJoinResult CreateSeed()
    {
        PreOobePlanValidator.ValidatePlan(plan);
        var binding = DomainJoinBinding.Validate(plan);
        return new()
        {
            OperationId = plan.OperationId,
            AttemptId = plan.AttemptId,
            PlanHash = planHash,
            ExpectedComputerName = binding.Parameters.ComputerName,
            ExpectedDomainName = binding.Parameters.DomainName,
            TargetOuDn = binding.Parameters.TargetOuDn,
            Cleanup = DomainJoinCleanupState.Pending
        };
    }
    public void Seed() { var seed = CreateSeed(); Validate(seed); DomainStateFile.Write(root, path, seed, create: true); }
    public DomainJoinResult Read()
    {
        var value = DomainStateFile.Read<DomainJoinResult>(root, path); Validate(value); return value;
    }
    public void Write(DomainJoinResult value)
    {
        Validate(value);
        var previous = Read();
        if (previous.OriginatingBootId.Length > 0 && previous.OriginatingBootId != value.OriginatingBootId ||
            previous.ComputerObjectGuid is { } guid && guid != value.ComputerObjectGuid || value.Restart < previous.Restart ||
            previous.Cleanup == DomainJoinCleanupState.Disposed && value.Cleanup != DomainJoinCleanupState.Disposed ||
            !CanAdvance(previous.Join, value.Join) || !CanAdvance(previous.Placement, value.Placement) || !CanAdvance(previous.Membership, value.Membership))
            throw new InvalidDataException("Domain report transition is invalid.");
        DomainStateFile.Write(root, path, value, create: false);
    }
    private static bool CanAdvance(DomainJoinPhaseResult previous, DomainJoinPhaseResult value) => previous == value ||
        previous.State == DomainJoinPhaseState.NotStarted ||
        previous.State == DomainJoinPhaseState.Unverified && value.State is DomainJoinPhaseState.Succeeded or DomainJoinPhaseState.Failed;
    private void Validate(DomainJoinResult value)
    {
        var expected = CreateSeed(); DomainJoinBinding.ValidateHash(planHash);
        if (value.SchemaVersion != 1 || value.OperationId != expected.OperationId || value.AttemptId != expected.AttemptId || value.PlanHash != planHash ||
            value.ExpectedComputerName != expected.ExpectedComputerName || value.ExpectedDomainName != expected.ExpectedDomainName || value.TargetOuDn != expected.TargetOuDn ||
            value.ComputerObjectGuid == Guid.Empty || !Enum.IsDefined(value.Restart) || !Enum.IsDefined(value.Cleanup))
            throw new InvalidDataException("Domain report identity is invalid.");
        DomainStateFile.ValidatePhase(value.Join); DomainStateFile.ValidatePhase(value.Placement); DomainStateFile.ValidatePhase(value.Membership);
        if (value.OriginatingBootId.Length == 0)
        {
            if (value.Join.State != DomainJoinPhaseState.NotStarted || value.Placement.State != DomainJoinPhaseState.NotStarted ||
                value.Membership.State != DomainJoinPhaseState.NotStarted || value.Restart != DomainJoinRestartState.NotRequired || value.ComputerObjectGuid is not null)
                throw new InvalidDataException("An unassigned report cannot contain execution.");
        }
        else DomainJoinBinding.ValidateBoot(value.OriginatingBootId);
        if (value.Join.State == DomainJoinPhaseState.Succeeded && value.Restart == DomainJoinRestartState.NotRequired ||
            value.Join.State != DomainJoinPhaseState.Succeeded && value.Placement.State == DomainJoinPhaseState.Succeeded)
            throw new InvalidDataException("Domain report phases are inconsistent.");
    }
}
