// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Serilog;

namespace Foundry.PostInstall.Execution;

public sealed class PreOobeOrchestrator(string root, string planHash, ExecutionJournal journal,
    IPreOobeActionExecutor executor, Func<string> bootIdentity, IProgress<PostInstallProgress>? progress = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private IProgress<PostInstallProgress>? progress = progress;
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;
    private bool isResuming;
    private DomainJoinBinding? domainBinding;
    private DomainJoinResultStore? domainResults;
    private DomainJoinResult? domainReport;
    private const int MaximumRestarts = 1024;
    public async Task<OrchestrationOutcome> RunAsync(PreOobeExecutionPlan plan, CancellationToken cancellationToken)
    {
        IDisposable lease;
        try { lease = journal.AcquireLease(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new("Unavailable", 3); }
        using (lease)
        {
            JournalState? state = null;
            string boot = string.Empty;
            bool validated = false;
            bool actionInFlight = false;
            var cleanup = new OwnedPayloadCleanup(root, journal);
            try
            {
                PreOobePlanValidator.ValidatePlan(plan);
                state = journal.Read();
                PreOobePlanValidator.ValidateState(plan, state, planHash);
                boot = bootIdentity();
                if (string.IsNullOrWhiteSpace(boot)) throw new InvalidDataException("Boot identity is unavailable.");
                validated = true;
                isResuming = state.RestartCount > 0 && state.BootIdentity != boot;
                bool domainResume = ReconcileDomain(plan, state, boot);
                Report(plan, state);
                if (domainResume && domainReport?.Restart is DomainJoinRestartState.Required or DomainJoinRestartState.Requested)
                {
                    if (state.DomainRestartBootIdentity == boot) return new("AwaitingRestart", 3);
                    state.BootIdentity = boot;
                    return await RequestRestartAsync(plan, state, 0, cancellationToken).ConfigureAwait(false);
                }
                if (domainResume)
                {
                    state.Status = "Running";
                    state.CompletionStatus = null;
                }
                if (state.Status is "Succeeded" or "CompletedWithErrors" or "Failed" or "Interrupted")
                    return await FinishWithBuiltInCleanupAsync(plan, state, boot, cleanup).ConfigureAwait(false);
                if (state.Status == "Completing")
                {
                    state.Status = state.CompletionStatus ?? throw new InvalidDataException("Completion state is missing.");
                    return await FinishWithBuiltInCleanupAsync(plan, state, boot, cleanup).ConfigureAwait(false);
                }
                if (state.Status == "AwaitingRestart")
                {
                    if (state.BootIdentity == boot) return new("AwaitingRestart", 3);
                }
                else if (state.Status != "Pending" && !domainResume)
                {
                    state.Status = "Interrupted";
                    state.UnsafePayloadBootIdentity = state.BootIdentity;
                    state.UnsafeActionId = state.Cursor < plan.Actions.Count ? plan.Actions[state.Cursor].Id : null;
                    journal.Write(state);
                    return await FinishWithBuiltInCleanupAsync(plan, state, boot, cleanup).ConfigureAwait(false);
                }
                else if (state.Status == "Pending")
                {
                    Report(plan, state, "Verifying");
                    await VerifyPackagesAsync(plan, cancellationToken).ConfigureAwait(false);
                }
                state.BootIdentity = boot;
                state.Status = "Running";
                journal.Write(state);
                while (state.Cursor < plan.Actions.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PreOobeExecutionAction action = plan.Actions[state.Cursor];
                    if (action.BuiltInKind == PreOobeBuiltInKind.Cleanup) break;
                    if (action.CustomAction?.Kind == PreOobeActionKind.Restart)
                    {
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        state.Actions[action.Id] = new() { Status = "Succeeded", StartedAtUtc = now, CompletedAtUtc = now };
                        state.Cursor++;
                        return await RequestRestartAsync(plan, state, action.CustomAction.RestartDelaySeconds, cancellationToken).ConfigureAwait(false);
                    }
                    // One wall-clock interval covers all private substeps, including a planned restart.
                    state.Actions[action.Id] = new()
                    {
                        Status = "Running",
                        StartedAtUtc = state.Actions.GetValueOrDefault(action.Id)?.StartedAtUtc ?? DateTimeOffset.UtcNow
                    };
                    foreach (PreOobeOwnedPayload payload in plan.OwnedPayloads.Where(payload => payload.IsSensitive && payload.ConsumerActionIds.Contains(action.Id)))
                        state.PayloadDispositions[payload.RelativePath] = "DisposalRequired";
                    journal.Write(state);
                    Report(plan, state);
                    Log.Information("Executing post-installation action {ActionId}; substep {Substep}", action.Id, state.Substep);
                    actionInFlight = true;
                    ActionStepOutcome outcome = await executor.ExecuteAsync(action, state.Substep, cancellationToken).ConfigureAwait(false);
                    actionInFlight = false;
                    state.HasWarnings |= outcome.HasWarnings;
                    if (outcome.TerminationUncertain || outcome.ExitCode == 1641)
                    {
                        state.UnsafePayloadBootIdentity = boot;
                        state.UnsafeActionId = action.Id;
                        outcome = outcome with { Succeeded = false, FailureCode = "execution_uncertain" };
                    }
                    bool domainAction = action.BuiltInKind is PreOobeBuiltInKind.DomainJoinAndPlacement or PreOobeBuiltInKind.VerifyDomainMembership;
                    if (domainAction)
                    {
                        domainReport = domainResults!.Read();
                        state.DomainReceiptGeneration = new DomainJoinPhaseStore(root, plan, planHash).Read().Generation;
                        state.HasWarnings |= DomainJoinResultStore.HasWarnings(domainReport);
                        LogDomainReport(domainReport);
                        if (action.BuiltInKind == PreOobeBuiltInKind.DomainJoinAndPlacement)
                            outcome = outcome with { RestartRequested = domainReport.Restart == DomainJoinRestartState.Required };
                    }
                    if (outcome.Succeeded && outcome.NextSubstep is int next)
                    {
                        if (next <= state.Substep || next > 10000) throw new InvalidDataException("Invalid internal cursor.");
                        state.Substep = next;
                        if (outcome.RestartRequested) return await RequestRestartAsync(plan, state, 0, cancellationToken).ConfigureAwait(false);
                        journal.Write(state);
                        continue;
                    }
                    state.Actions[action.Id] = state.Actions[action.Id] with
                    {
                        Status = outcome.Succeeded ? "Succeeded" : "Failed",
                        ExitCode = outcome.ExitCode,
                        FailureCode = outcome.FailureCode,
                        CompletedAtUtc = DateTimeOffset.UtcNow
                    };
                    state.Cursor++;
                    state.Substep = 0;
                    journal.Write(state);
                    Report(plan, state);
                    Log.Information("Post-installation action {ActionId} finished with {Status}; exit code {ExitCode}",
                        action.Id, state.Actions[action.Id].Status, outcome.ExitCode);
                    if (DisposePayloads(plan, state, false, boot, cleanup).HasFatalSensitiveFailure) throw new IOException("Sensitive input disposal failed.");
                    bool mayContinue = action.CustomAction?.Process?.ErrorPolicy == PreOobeErrorPolicy.Continue ||
                        action.BuiltInKind is PreOobeBuiltInKind.Activation || domainAction;
                    bool uncertainNow = state.UnsafePayloadBootIdentity == boot && state.UnsafeActionId == action.Id;
                    if (!outcome.Succeeded && (!mayContinue || uncertainNow && !domainAction))
                    {
                        state.Status = "Failed";
                        journal.Write(state);
                        return await FinishWithBuiltInCleanupAsync(plan, state, boot, cleanup).ConfigureAwait(false);
                    }
                    if (outcome.RestartRequested)
                    {
                        if (action.CustomAction?.Process?.RestartTiming == PreOobeRestartTiming.Deferred)
                        { state.DeferredRestart = true; journal.Write(state); }
                        else return await RequestRestartAsync(plan, state, 0, cancellationToken).ConfigureAwait(false);
                    }
                }
                if (state.DeferredRestart) return await RequestRestartAsync(plan, state, 0, cancellationToken).ConfigureAwait(false);
                state.Status = state.HasWarnings || state.Actions.Values.Any(result => result.Status == "Failed")
                    ? "CompletedWithErrors" : "Succeeded";
                return await FinishWithBuiltInCleanupAsync(plan, state, boot, cleanup).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Log.Error("Post-installation stopped; failure type {FailureType}", ex.GetType().Name);
                if (validated && state is not null)
                {
                    if (actionInFlight || state.Status == "Running" && state.Cursor < plan.Actions.Count &&
                        state.Actions.GetValueOrDefault(plan.Actions[state.Cursor].Id)?.Status == "Running")
                    {
                        state.UnsafePayloadBootIdentity = boot;
                        state.UnsafeActionId = state.Cursor < plan.Actions.Count ? plan.Actions[state.Cursor].Id : null;
                    }
                    state.Status = "Failed";
                    try { journal.Write(state); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                    try { if (DisposePayloads(plan, state, true, boot, cleanup).HasFatalSensitiveFailure) state.Status = "Failed"; }
                    catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                    { Log.Error("Post-installation cleanup could not be published; failure type {FailureType}", error.GetType().Name); }
                    Report(plan, state);
                }
                return new("Failed", 3);
            }
        }
    }

    private bool ReconcileDomain(PreOobeExecutionPlan plan, JournalState state, string boot)
    {
        if (!plan.Actions.Any(action => action.BuiltInKind == PreOobeBuiltInKind.DomainJoinAndPlacement)) return false;
        domainBinding = DomainJoinBinding.Validate(plan);
        var phases = new DomainJoinPhaseStore(root, plan, planHash);
        var join = state.Actions.GetValueOrDefault(domainBinding.JoinAction.Id);
        bool inspectWorker = join?.Status == "Running" && state.BootIdentity == boot;
        using var workerLease = inspectWorker ? TryAcquireWorkerLease(phases) : null;
        var receipt = phases.Read();
        if (state.DomainReceiptGeneration is { } generation && (generation < 0 || receipt.Generation < generation))
            throw new InvalidDataException("Domain receipt generation moved backwards.");
        domainResults = new(root, plan, planHash);
        domainReport = domainResults.Read();
        if (join is null || join.Status == "Skipped" && domainReport.Join.State == DomainJoinPhaseState.NotStarted)
        {
            if (receipt.OriginatingBootId is not null || domainReport.Join.State != DomainJoinPhaseState.NotStarted ||
                state.DomainRestartBootIdentity is not null || state.DomainReceiptGeneration is not null)
                throw new InvalidDataException("Domain execution has no journaled owner.");
            return false;
        }
        string origin = domainReport.OriginatingBootId.Length > 0 ? domainReport.OriginatingBootId :
            state.BootIdentity ?? throw new InvalidDataException("Domain execution origin is missing.");
        DomainJoinBinding.ValidateBoot(origin);
        domainReport = domainResults.Reconcile(receipt, origin, workerUnsettled: inspectWorker && workerLease is null);
        state.DomainReceiptGeneration = receipt.Generation;
        if (state.DomainRestartBootIdentity is { } requested && (requested != origin || state.RestartCount == 0))
            throw new InvalidDataException("Domain restart checkpoint is invalid.");
        if (domainReport.Restart is DomainJoinRestartState.Required or DomainJoinRestartState.Requested)
        {
            var restart = boot != origin ? DomainJoinRestartState.Completed :
                state.DomainRestartBootIdentity is not null ? DomainJoinRestartState.Requested : DomainJoinRestartState.Required;
            if (domainReport.Restart == DomainJoinRestartState.Requested && state.DomainRestartBootIdentity is null)
                throw new InvalidDataException("Domain restart publication is missing.");
            domainReport = domainReport with { Restart = restart };
            domainResults.Write(domainReport);
        }
        bool warning = DomainJoinResultStore.HasWarnings(domainReport);
        state.HasWarnings |= warning;
        LogDomainReport(domainReport);
        int joinIndex = plan.Actions.ToList().FindIndex(action => action.Id == domainBinding.JoinAction.Id);
        if (state.Cursor < joinIndex) throw new InvalidDataException("Domain cursor precedes its recorded execution.");
        if (join.Status == "Running")
        {
            bool settledPrepared = workerLease is not null && receipt.Phase == DomainJoinReceiptPhase.Prepared &&
                domainReport.Join.State == DomainJoinPhaseState.Failed;
            if (settledPrepared)
            {
                if (state.UnsafePayloadBootIdentity == origin && state.UnsafeActionId == domainBinding.JoinAction.Id)
                {
                    state.UnsafePayloadBootIdentity = null;
                    state.UnsafeActionId = null;
                }
                else if (state.UnsafePayloadBootIdentity == boot)
                {
                    state.Status = "Failed";
                    state.CompletionStatus = null;
                    journal.Write(state);
                    return false;
                }
            }
            else if (state.UnsafePayloadBootIdentity != boot || state.UnsafeActionId == domainBinding.JoinAction.Id)
            {
                state.UnsafePayloadBootIdentity = origin;
                state.UnsafeActionId = domainBinding.JoinAction.Id;
            }
            state.Actions[domainBinding.JoinAction.Id] = join with
            {
                Status = warning ? "Failed" : "Succeeded",
                FailureCode = warning ? "domain_join_warning" : null,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }
        if (state.Cursor == joinIndex) { state.Cursor++; state.Substep = 0; }
        bool resume = state.Cursor < plan.Actions.Count && plan.Actions[state.Cursor].Id == domainBinding.VerificationAction.Id;
        if (resume && domainReport.Membership.State is DomainJoinPhaseState.Succeeded or DomainJoinPhaseState.Failed or DomainJoinPhaseState.Skipped)
        {
            state.Actions[domainBinding.VerificationAction.Id] = new() { Status = domainReport.Membership.State == DomainJoinPhaseState.Failed ? "Failed" : "Succeeded" };
            state.Cursor++;
        }
        journal.Write(state);
        return resume;
    }

    /// <summary>Records the password-free phase outcomes; only states, allowlisted codes and numeric errors are written.</summary>
    private static void LogDomainReport(DomainJoinResult report)
    {
        Log.Information(
            "Domain join report; join {JoinState} ({JoinFailure}), placement {PlacementState} ({PlacementFailure}), " +
            "membership {MembershipState} ({MembershipFailure}), restart {Restart}, cleanup {Cleanup}",
            report.Join.State, report.Join.FailureCode, report.Placement.State, report.Placement.FailureCode,
            report.Membership.State, report.Membership.FailureCode, report.Restart, report.Cleanup);
        foreach (var (phase, result) in new[] { ("join", report.Join), ("placement", report.Placement), ("membership", report.Membership) })
        {
            if (result.NativeErrorCode is null && result.LdapErrorCode is null && result.DirectoryResultCode is null) continue;
            Log.Warning("Domain {Phase} error codes; native {NativeErrorCode}, LDAP {LdapErrorCode}, directory result {DirectoryResultCode}",
                phase, result.NativeErrorCode, result.LdapErrorCode, result.DirectoryResultCode);
        }
    }

    private static IDisposable? TryAcquireWorkerLease(DomainJoinPhaseStore phases)
    {
        try { return phases.AcquireWorkerLease(); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return null; }
    }

    private OwnedPayloadCleanupOutcome DisposePayloads(PreOobeExecutionPlan plan, JournalState state, bool terminal, string boot, OwnedPayloadCleanup cleanup)
    {
        var outcome = cleanup.Dispose(plan, state, terminal, boot);
        state.HasWarnings |= outcome.HasDomainCleanupPending;
        if (domainResults is not null && domainBinding is not null)
        {
            domainReport = domainResults.Read();
            var disposition = state.PayloadDispositions.GetValueOrDefault(domainBinding.Parameters.CredentialPayloadPath);
            var next = disposition == "Disposed" ? DomainJoinCleanupState.Disposed : DomainJoinCleanupState.Pending;
            if (domainReport.Cleanup != next) domainResults.Write(domainReport = domainReport with { Cleanup = next });
        }
        return outcome;
    }

    /// <summary>Commits the next cursor before waiting; cancelling the display delay must not invalidate a durable restart.</summary>
    private async Task<OrchestrationOutcome> RequestRestartAsync(PreOobeExecutionPlan plan, JournalState state,
        int seconds, CancellationToken cancellationToken)
    {
        if (++state.RestartCount > MaximumRestarts) throw new InvalidDataException("Restart budget exceeded.");
        state.DeferredRestart = false;
        state.Status = "AwaitingRestart";
        bool domainRestart = domainReport?.Restart == DomainJoinRestartState.Required;
        if (domainRestart) state.DomainRestartBootIdentity = domainReport!.OriginatingBootId;
        try { journal.Write(state); }
        catch
        {
            // Do not publish an uncommitted restart marker through the outer failure handler.
            var durable = journal.Read();
            state.RestartCount = durable.RestartCount;
            state.DomainRestartBootIdentity = durable.DomainRestartBootIdentity;
            throw;
        }
        if (domainRestart)
        {
            domainReport = domainReport! with { Restart = DomainJoinRestartState.Requested };
            domainResults!.Write(domainReport);
        }
        Log.Information("Post-installation checkpoint saved for restart {RestartCount}; delay {DelaySeconds} seconds",
            state.RestartCount, seconds);
        try
        {
            for (int remaining = seconds; remaining > 0; remaining--)
            {
                Report(plan, state, restartSecondsRemaining: remaining);
                await delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report(plan, state);
            return new("AwaitingRestart", 3);
        }
        Report(plan, state, restartSecondsRemaining: 0);
        return new("AwaitingRestart", 2);
    }

    private async Task<OrchestrationOutcome> FinishWithBuiltInCleanupAsync(PreOobeExecutionPlan plan, JournalState state,
        string boot, OwnedPayloadCleanup cleanup)
    {
        state.CompletionStatus = state.Status;
        state.Status = "Completing";
        journal.Write(state);
        if (state.UnsafePayloadBootIdentity != boot)
        {
            foreach (PreOobeExecutionAction action in plan.Actions.Where(action => action.BuiltInKind == PreOobeBuiltInKind.Cleanup))
            {
                if (state.Actions.GetValueOrDefault(action.Id)?.Status == "Succeeded") continue;
                state.Actions[action.Id] = new() { Status = "Running", StartedAtUtc = DateTimeOffset.UtcNow };
                journal.Write(state);
                Report(plan, state);
                ActionStepOutcome result = await executor.ExecuteAsync(action, 0, CancellationToken.None).ConfigureAwait(false);
                if (result.TerminationUncertain)
                {
                    state.UnsafePayloadBootIdentity = boot;
                    state.UnsafeActionId = null;
                    state.CompletionStatus = "Failed";
                }
                state.Actions[action.Id] = state.Actions[action.Id] with
                {
                    Status = result.Succeeded ? "Succeeded" : "Failed",
                    FailureCode = result.FailureCode,
                    ExitCode = result.ExitCode,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                state.HasWarnings |= !result.Succeeded || result.HasWarnings;
                journal.Write(state);
            }
        }
        state.Status = state.CompletionStatus;
        return Finish(plan, state, boot, cleanup);
    }

    private OrchestrationOutcome Finish(PreOobeExecutionPlan plan, JournalState state, string boot, OwnedPayloadCleanup cleanup)
    {
        if (DisposePayloads(plan, state, true, boot, cleanup).HasFatalSensitiveFailure) state.Status = "Failed";
        foreach (PreOobeExecutionAction action in plan.Actions)
            if (!state.Actions.ContainsKey(action.Id)) state.Actions[action.Id] = new() { Status = "Skipped" };
        if (state.Status == "Succeeded" && state.HasWarnings) state.Status = "CompletedWithErrors";
        state.CompletionStatus = null;
        journal.Write(state);
        Report(plan, state);
        return new(state.Status, state.Status is "Succeeded" or "CompletedWithErrors" ? 0 : 3);
    }

    private void Report(PreOobeExecutionPlan plan, JournalState state, string? status = null, int? restartSecondsRemaining = null)
    {
        if (progress is null) return;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PostInstallActionProgress[] actions = plan.Actions.Select(action =>
            {
                var result = state.Actions.GetValueOrDefault(action.Id);
                string actionStatus = result?.Status ?? "Waiting";
                if (actionStatus == "Running" && state.Status is "Failed" or "Interrupted") actionStatus = "Interrupted";
                TimeSpan? elapsed = result?.StartedAtUtc is { } start ? (result.CompletedAtUtc ?? now) - start : null;
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                return new PostInstallActionProgress(action.Id, PostInstallProgress.GetActionName(action), actionStatus, elapsed, result?.ExitCode);
            }).ToArray();
            int warnings = state.Actions.Count(pair => pair.Key != domainBinding?.JoinAction.Id && pair.Key != domainBinding?.VerificationAction.Id &&
                (pair.Value.Status == "Failed" || pair.Value.FailureCode is not null));
            if (domainReport is not null)
            {
                warnings += new[] { domainReport.Join, domainReport.Placement, domainReport.Membership }.Count(phase =>
                    phase.State is DomainJoinPhaseState.Failed or DomainJoinPhaseState.Unknown or DomainJoinPhaseState.Unverified);
                if (state.PayloadDispositions.GetValueOrDefault(domainBinding!.Parameters.CredentialPayloadPath) == "CleanupPending") warnings++;
            }
            if (state.HasWarnings) warnings = Math.Max(1, warnings);
            progress.Report(new(actions, status ?? state.Status, isResuming, restartSecondsRemaining, domainReport, warnings));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            progress = null;
            Log.Warning("Post-installation progress display is unavailable; failure type {FailureType}", error.GetType().Name);
        }
    }

    private async Task VerifyPackagesAsync(PreOobeExecutionPlan plan, CancellationToken cancellationToken)
    {
        foreach (PreOobeStagedPackage package in plan.Packages)
        {
            string packageRoot = OwnedPaths.Resolve(root, package.RelativePath);
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new Stack<string>();
            directories.Push(packageRoot);
            while (directories.TryPop(out string? directory))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    OwnedPaths.RejectReparsePoints(packageRoot, entry);
                    if (Directory.Exists(entry)) directories.Push(entry);
                    else actual.Add(Path.GetRelativePath(packageRoot, entry).Replace('\\', '/'));
                    if (actual.Count > 100000) throw new InvalidDataException("Staged content has too many files.");
                }
            }
            if (!actual.SetEquals(package.Manifest.Files.Select(file => file.RelativePath.Replace('\\', '/'))))
                throw new InvalidDataException("Staged content inventory mismatch.");
            foreach (PreOobePackageFile file in package.Manifest.Files)
            {
                string path = OwnedPaths.Resolve(packageRoot, file.RelativePath);
                if (new FileInfo(path).Length != file.Length) throw new InvalidDataException("Staged content size mismatch.");
                await using var stream = File.OpenRead(path);
                string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged content hash mismatch.");
            }
        }
    }
}
