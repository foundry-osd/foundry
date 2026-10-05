// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;

namespace Foundry.PostInstall.Execution;

/// <summary>Fixed-path durable receipts; callers hold the separate worker lease while binding and advancing them.</summary>
internal sealed class DomainJoinPhaseStore(string root, PreOobeExecutionPlan plan, string planHash)
{
    private readonly string path = OwnedPaths.Resolve(root, "State/PreOobe/domain-join-phase.json");
    /// <summary>Describes the unassigned receipt Deploy staged; the installed boot is bound only by the worker.</summary>
    public DomainJoinPhaseReceipt CreateSeed() =>
        DomainJoinPhaseReceipt.CreateSeed(plan.OperationId, plan.AttemptId, planHash, DomainJoinBinding.Validate(plan).JoinAction.Id);
    /// <summary>Serializes domain workers independently of the parent-held runner lease.</summary>
    public IDisposable AcquireWorkerLease() => new FileStream(OwnedPaths.Resolve(root, "State/PreOobe/domain-worker.lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    public DomainJoinPhaseReceipt Read()
    {
        var value = DomainStateFile.Read<DomainJoinPhaseReceipt>(root, path); Validate(value); return value;
    }
    public void Write(DomainJoinPhaseReceipt value)
    {
        Validate(value);
        var previous = Read();
        bool binding = previous.Phase == DomainJoinReceiptPhase.Prepared && previous.OriginatingBootId is null &&
            value.Phase == DomainJoinReceiptPhase.Prepared && value.OriginatingBootId is not null;
        bool advance = previous.OriginatingBootId is not null && previous.OriginatingBootId == value.OriginatingBootId &&
            (previous.Phase, value.Phase) is
                (DomainJoinReceiptPhase.Prepared, DomainJoinReceiptPhase.JoinStarted or DomainJoinReceiptPhase.Finished) or
                (DomainJoinReceiptPhase.JoinStarted, DomainJoinReceiptPhase.JoinReturned) or
                (DomainJoinReceiptPhase.JoinReturned, DomainJoinReceiptPhase.PlacementStarted or DomainJoinReceiptPhase.Finished) or
                (DomainJoinReceiptPhase.PlacementStarted, DomainJoinReceiptPhase.PlacementReturned) or
                (DomainJoinReceiptPhase.PlacementReturned, DomainJoinReceiptPhase.Finished);
        if (value.Generation != checked(previous.Generation + 1) || !(binding || advance) ||
            previous.RestartRequired && !value.RestartRequired ||
            previous.ComputerObjectGuid is { } computer && computer != value.ComputerObjectGuid ||
            previous.DestinationObjectGuid is { } destination && destination != value.DestinationObjectGuid ||
            previous.Join.State != DomainJoinPhaseState.NotStarted && previous.Join != value.Join ||
            previous.Placement.State != DomainJoinPhaseState.NotStarted && previous.Placement != value.Placement)
            throw new InvalidDataException("Domain receipt transition is invalid.");
        DomainStateFile.Write(root, path, value, create: false);
    }
    /// <summary>Binds one unstarted seed to the validated Running journal before any mutation.</summary>
    public DomainJoinPhaseReceipt BindOrigin(string boot)
    {
        DomainJoinBinding.ValidateBoot(boot);
        var seed = Read();
        if (seed.OriginatingBootId is not null || seed.Phase != DomainJoinReceiptPhase.Prepared || seed.Generation != 0)
            throw new InvalidDataException("The domain worker cannot replay this receipt.");
        DomainJoinBinding.RequireRunning(root, plan, planHash, boot, seed.ActionId);
        var value = seed with { OriginatingBootId = boot, Generation = 1 };
        Write(value); return value;
    }
    private void Validate(DomainJoinPhaseReceipt value)
    {
        var binding = DomainJoinBinding.Validate(plan); DomainJoinBinding.ValidateHash(planHash);
        if (value.SchemaVersion != 1 || value.OperationId != plan.OperationId || value.AttemptId != plan.AttemptId ||
            value.PlanHash != planHash || value.ActionId != binding.JoinAction.Id || value.Generation < 0 ||
            !Enum.IsDefined(value.Phase) || value.ComputerObjectGuid == Guid.Empty || value.DestinationObjectGuid == Guid.Empty)
            throw new InvalidDataException("Domain receipt identity is invalid.");
        DomainStateFile.ValidatePhase(value.Join); DomainStateFile.ValidatePhase(value.Placement);
        if (value.OriginatingBootId is null)
        {
            if (value.Generation != 0 || value.Phase != DomainJoinReceiptPhase.Prepared || value.ComputerObjectGuid is not null || value.DestinationObjectGuid is not null)
                throw new InvalidDataException("An unassigned receipt cannot contain execution.");
        }
        else { DomainJoinBinding.ValidateBoot(value.OriginatingBootId); if (value.Generation == 0) throw new InvalidDataException("Domain receipt generation is invalid."); }
        bool beforeReturn = value.Phase is DomainJoinReceiptPhase.Prepared or DomainJoinReceiptPhase.JoinStarted;
        if (beforeReturn && (value.Join.State != DomainJoinPhaseState.NotStarted || value.Placement.State != DomainJoinPhaseState.NotStarted || value.RestartRequired) ||
            value.Phase == DomainJoinReceiptPhase.Prepared && (value.ComputerObjectGuid is not null || value.DestinationObjectGuid is not null) ||
            !beforeReturn && value.Join.State is not (DomainJoinPhaseState.Succeeded or DomainJoinPhaseState.Failed or DomainJoinPhaseState.Unknown) ||
            value.RestartRequired != (value.Join.State == DomainJoinPhaseState.Succeeded) ||
            value.Phase is DomainJoinReceiptPhase.PlacementStarted or DomainJoinReceiptPhase.PlacementReturned &&
                (value.Join.State != DomainJoinPhaseState.Succeeded || value.ComputerObjectGuid is null || value.DestinationObjectGuid is null) ||
            value.Phase is DomainJoinReceiptPhase.JoinReturned or DomainJoinReceiptPhase.PlacementStarted && value.Placement.State != DomainJoinPhaseState.NotStarted ||
            value.Phase is DomainJoinReceiptPhase.PlacementReturned or DomainJoinReceiptPhase.Finished && value.Placement.State == DomainJoinPhaseState.NotStarted ||
            value.Join.State is DomainJoinPhaseState.Failed or DomainJoinPhaseState.Unknown && value.Placement.State is not (DomainJoinPhaseState.NotStarted or DomainJoinPhaseState.Skipped))
            throw new InvalidDataException("Domain receipt phases are inconsistent.");
    }

}

/// <summary>Bounded, durable, atomic replacement shared by the two fixed domain state files.</summary>
internal static class DomainStateFile
{
    public static T Read<T>(string root, string path)
    {
        OwnedPaths.RejectReparsePoints(root, path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > DomainJoinConfigurationValidator.MaximumResultBytes) throw new InvalidDataException("Domain state size is invalid.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        return JsonSerializer.Deserialize<T>(bytes, ExecutionJournal.JsonOptions) ?? throw new InvalidDataException("Domain state is empty.");
    }
    public static void Write<T>(string root, string path, T value, bool create)
    {
        OwnedPaths.RejectReparsePoints(root, path);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, ExecutionJournal.JsonOptions);
        if (bytes.Length > DomainJoinConfigurationValidator.MaximumResultBytes) throw new InvalidDataException("Domain state size is invalid.");
        string temporary = OwnedPaths.Resolve(root, "State/PreOobe/.domain-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (create) File.Move(temporary, path); else File.Replace(temporary, path, null);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }
    public static void ValidatePhase(DomainJoinPhaseResult value)
    {
        if (value is null || new[] { value.NativeErrorCode, value.LdapErrorCode, value.DirectoryResultCode }.Count(code => code.HasValue) > 1 || !Enum.IsDefined(value.State) || value.FailureCode is { } code && !Enum.IsDefined(code) ||
            value.State is DomainJoinPhaseState.NotStarted or DomainJoinPhaseState.Succeeded or DomainJoinPhaseState.Skipped &&
                (value.FailureCode is not null || value.NativeErrorCode is not null || value.LdapErrorCode is not null || value.DirectoryResultCode is not null) ||
            value.State is DomainJoinPhaseState.Failed or DomainJoinPhaseState.Unknown or DomainJoinPhaseState.Unverified && value.FailureCode is null)
            throw new InvalidDataException("Domain phase result is invalid.");
    }
}
