// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Windows;

/// <summary>Reports independent join and placement observations without credentials.</summary>
internal sealed record DomainJoinWorkerResult(DomainJoinPhaseResult Join, DomainJoinPhaseResult Placement, Guid? ComputerObjectGuid, bool RestartRequired);

/// <summary>Runs one operation-bound online join; parent process supervision supplies the hard wall-clock bound.</summary>
internal sealed class DomainJoinWorker(string root, PreOobeExecutionPlan plan, string planHash, string originatingBoot,
    INativeDomainJoin native, IDomainComputerAccountDirectory directory, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>
    /// Cooperative budget checked between native calls, which cannot be interrupted; the supervising parent waits
    /// slightly longer before terminating the worker.
    /// </summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan ReadinessBudget = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ReadinessRetryInterval = TimeSpan.FromSeconds(5);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;
    private DomainDirectoryException? lastReadinessFailure;

    public async Task<DomainJoinWorkerResult> RunAsync(DomainJoinActionParameters parameters, CancellationToken token)
    {
        var snapshot = await PreOobePlanLoader.LoadAsync(root, token).ConfigureAwait(false);
        PreOobePlanLoader.RequireMatching(snapshot, plan, planHash);
        var binding = DomainJoinBinding.Validate(plan);
        if (snapshot.Hash != planHash || DomainJoinBinding.Validate(snapshot.Plan).Parameters != parameters || binding.Parameters != parameters)
            throw new InvalidDataException("The worker plan binding changed.");
        DomainJoinBinding.RequireRunning(root, plan, planHash, originatingBoot, binding.JoinAction.Id);
        var store = new DomainJoinPhaseStore(root, plan, planHash);
        using var lease = store.AcquireWorkerLease();
        var receipt = store.BindOrigin(originatingBoot);
        using (directory)
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            budget.CancelAfter(Budget);
            DomainJoinCredentialPayload? credentials = null;
            DomainDirectoryReady ready;
            try
            {
                byte[] bytes = await PreOobePlanLoader.ReadBoundedAsync(OwnedPaths.Resolve(root, parameters.CredentialPayloadPath),
                    DomainJoinConfigurationValidator.MaximumCredentialPayloadBytes, budget.Token).ConfigureAwait(false);
                try { credentials = DomainJoinCredentialPayloadCodec.Decode(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                if (!credentials.Context.Matches(new(parameters.DomainName, credentials.Context.AccountName)))
                {
                    credentials.Dispose();
                    return Finish(Failure(DomainJoinPhaseState.Failed, DomainJoinFailureCode.ContextMismatch), Skipped());
                }
                using var readiness = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                readiness.CancelAfter(ReadinessBudget);
                ready = await WaitForDirectoryAsync(credentials, parameters, readiness.Token).ConfigureAwait(false);
                readiness.Token.ThrowIfCancellationRequested();
                ValidateReady(ready, parameters);
            }
            catch (Exception error) when (Recoverable(error))
            {
                credentials?.Dispose();
                // A timeout keeps the numeric code of the last unreachable attempt, which is the actual cause.
                bool timedOut = error is OperationCanceledException;
                return Finish(Failure(DomainJoinPhaseState.Failed, timedOut ? DomainJoinFailureCode.ReadinessTimeout :
                    credentials is null ? DomainJoinFailureCode.CredentialUnavailable : DomainJoinFailureCode.DomainUnavailable,
                    timedOut ? lastReadinessFailure ?? error : error), Skipped());
            }
            using (credentials)
            {
                bool pending;
                try { budget.Token.ThrowIfCancellationRequested(); pending = !string.Equals(native.GetActiveComputerName(), parameters.ComputerName, StringComparison.OrdinalIgnoreCase); }
                catch (Exception error) when (Recoverable(error)) { return Finish(Failure(DomainJoinPhaseState.Failed, DomainJoinFailureCode.ComputerNameMismatch, error), Skipped()); }
                receipt = receipt with { ComputerObjectGuid = ready.Computer?.Guid, DestinationObjectGuid = ready.Destination?.Guid };
                Save(DomainJoinReceiptPhase.JoinStarted);
                DomainJoinPhaseResult join;
                try
                {
                    budget.Token.ThrowIfCancellationRequested();
                    int status = pending ? native.SetComputerName(parameters.ComputerName) : 0;
                    if (status != 0) join = Failure(DomainJoinPhaseState.Failed, DomainJoinFailureCode.ComputerNameMismatch) with { NativeErrorCode = status };
                    else
                    {
                        budget.Token.ThrowIfCancellationRequested();
                        status = native.Join(ready.Domain + "\\" + ready.Controller, ready.Computer is null ? ready.Destination?.DistinguishedName : null,
                            credentials.Context, credentials.Password.Span, pending);
                        join = status == 0 ? new() { State = DomainJoinPhaseState.Succeeded } : Failure(DomainJoinPhaseState.Failed, DomainJoinFailureCode.JoinFailed) with { NativeErrorCode = status };
                    }
                }
                catch (Exception error) when (Recoverable(error)) { join = Failure(DomainJoinPhaseState.Unknown, DomainJoinFailureCode.Interrupted, error); }
                credentials.Dispose();
                receipt = receipt with { Join = join, RestartRequired = join.State == DomainJoinPhaseState.Succeeded };
                Save(DomainJoinReceiptPhase.JoinReturned);
                if (join.State != DomainJoinPhaseState.Succeeded) return Finish(join, Skipped());
                if (ready.Destination is null) return Finish(join, Skipped());
                DomainJoinPhaseResult placement;
                try
                {
                    budget.Token.ThrowIfCancellationRequested();
                    var target = await directory.ReadDestinationAsync(ready.Destination.Guid, budget.Token).ConfigureAwait(false);
                    ValidateObject(target, parameters.DomainName);
                    if (target.Guid != ready.Destination.Guid) throw new InvalidDataException("The destination identity changed.");
                    if (ready.Computer is null)
                    {
                        var created = await directory.FindComputerAsync(parameters.ComputerName, budget.Token).ConfigureAwait(false);
                        if (created is not null) { ValidateObject(created, parameters.DomainName); receipt = receipt with { ComputerObjectGuid = created.Guid }; }
                        placement = created?.ParentGuid == target.Guid ? new() { State = DomainJoinPhaseState.Succeeded } : Failure(DomainJoinPhaseState.Unverified, DomainJoinFailureCode.PlacementFailed);
                    }
                    else
                    {
                        var current = await directory.ReadComputerAsync(ready.Computer.Guid, budget.Token).ConfigureAwait(false);
                        ValidateObject(current, parameters.DomainName);
                        if (current.Guid != ready.Computer.Guid) throw new InvalidDataException("The computer identity changed.");
                        if (current.ParentGuid == target.Guid) placement = new() { State = DomainJoinPhaseState.Succeeded };
                        else
                        {
                            Save(DomainJoinReceiptPhase.PlacementStarted);
                            bool acknowledged = false;
                            try
                            {
                                await directory.MoveAsync(current, target, budget.Token).ConfigureAwait(false);
                                acknowledged = true;
                                placement = new() { State = DomainJoinPhaseState.Succeeded };
                            }
                            catch (Exception error) when (Recoverable(error))
                            {
                                placement = Failure(error is DomainDirectoryException { Rejected: true } ? DomainJoinPhaseState.Failed : DomainJoinPhaseState.Unknown,
                                    DomainJoinFailureCode.PlacementFailed, error);
                            }
                            bool verified = await ReadBackAsync(current.Guid, target.Guid, budget.Token).ConfigureAwait(false);
                            if (acknowledged && !verified) placement = Failure(DomainJoinPhaseState.Unverified, DomainJoinFailureCode.PlacementFailed);
                            receipt = receipt with { Placement = placement }; Save(DomainJoinReceiptPhase.PlacementReturned);
                        }
                    }
                }
                catch (Exception error) when (Recoverable(error) && receipt.Phase == DomainJoinReceiptPhase.JoinReturned)
                { placement = Failure(DomainJoinPhaseState.Failed, DomainJoinFailureCode.PlacementFailed, error); }
                return Finish(join, placement);
            }
        }

        void Save(DomainJoinReceiptPhase phase)
        {
            var next = receipt with { Phase = phase, Generation = checked(receipt.Generation + 1) };
            store.Write(next); receipt = next;
        }
        DomainJoinWorkerResult Finish(DomainJoinPhaseResult join, DomainJoinPhaseResult placement)
        {
            receipt = receipt with { Join = join, Placement = placement };
            Save(DomainJoinReceiptPhase.Finished);
            return new(receipt.Join, receipt.Placement, receipt.ComputerObjectGuid, receipt.RestartRequired);
        }
    }

    /// <summary>
    /// Repeats read-only readiness while the installed network or a domain controller is still becoming reachable.
    /// Credential and directory rejections are never repeated, so a wrong password cannot lock the account.
    /// </summary>
    private async Task<DomainDirectoryReady> WaitForDirectoryAsync(DomainJoinCredentialPayload credentials,
        DomainJoinActionParameters parameters, CancellationToken token)
    {
        while (true)
        {
            try
            {
                return await directory.PrepareAsync(credentials.Context, credentials.Password, parameters.ComputerName,
                    parameters.TargetOuDn, token).ConfigureAwait(false);
            }
            catch (DomainDirectoryException error) when (error.Transient)
            {
                lastReadinessFailure = error;
                await delay(ReadinessRetryInterval, token).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> ReadBackAsync(Guid computer, Guid target, CancellationToken token)
    {
        for (int attempt = 0; attempt < 3 && !token.IsCancellationRequested; attempt++)
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token); request.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var observed = await directory.ReadComputerAsync(computer, request.Token).ConfigureAwait(false);
                if (observed.Guid == computer && observed.ParentGuid == target) return true;
            }
            catch (Exception error) when (Recoverable(error)) { }
        }
        return false;
    }
    private static void ValidateReady(DomainDirectoryReady ready, DomainJoinActionParameters parameters)
    {
        if (DomainJoinCredentialContext.CanonicalizeDomainName(ready.Domain) != DomainJoinCredentialContext.CanonicalizeDomainName(parameters.DomainName) ||
            !DomainJoinCredentialContext.IsValidDomainName(ready.Controller) || (parameters.TargetOuDn is null) != (ready.Destination is null))
            throw new InvalidDataException("Directory readiness is invalid.");
        if (ready.Destination is not null) ValidateObject(ready.Destination, parameters.DomainName);
        if (ready.Computer is not null) ValidateObject(ready.Computer, parameters.DomainName);
    }
    private static void ValidateObject(DomainDirectoryObject value, string domain)
    {
        if (value.Guid == Guid.Empty || !DistinguishedNameRules.IsWithinDomain(value.DistinguishedName, domain)) throw new InvalidDataException("Directory identity is invalid.");
    }
    internal static bool Recoverable(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    internal static DomainJoinPhaseResult Failure(DomainJoinPhaseState state, DomainJoinFailureCode code, Exception? error = null) => new()
    {
        State = state,
        FailureCode = code,
        NativeErrorCode = (error as DomainDirectoryException)?.NativeErrorCode ?? (error as System.ComponentModel.Win32Exception)?.NativeErrorCode,
        LdapErrorCode = (error as DomainDirectoryException)?.LdapErrorCode,
        DirectoryResultCode = (error as DomainDirectoryException)?.DirectoryResultCode
    };
    private static DomainJoinPhaseResult Skipped() => new() { State = DomainJoinPhaseState.Skipped };
}
