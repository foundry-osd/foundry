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
                Report(plan, state);
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
                else if (state.Status != "Pending")
                {
                    state.Status = "Interrupted";
                    state.UnsafePayloadBootIdentity = state.BootIdentity;
                    state.UnsafeActionId = state.Cursor < plan.Actions.Count ? plan.Actions[state.Cursor].Id : null;
                    journal.Write(state);
                    return await FinishWithBuiltInCleanupAsync(plan, state, boot, cleanup).ConfigureAwait(false);
                }
                else
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
                    if (!cleanup.Dispose(plan, state, false, boot)) throw new IOException("Sensitive input disposal failed.");
                    bool mayContinue = action.CustomAction?.Process?.ErrorPolicy == PreOobeErrorPolicy.Continue ||
                        action.BuiltInKind is PreOobeBuiltInKind.Activation;
                    if (!outcome.Succeeded && (!mayContinue || state.UnsafePayloadBootIdentity == boot))
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
                    if (actionInFlight)
                    {
                        state.UnsafePayloadBootIdentity = boot;
                        state.UnsafeActionId = state.Cursor < plan.Actions.Count ? plan.Actions[state.Cursor].Id : null;
                    }
                    state.Status = "Failed";
                    try { journal.Write(state); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                    cleanup.Dispose(plan, state, true, boot);
                    Report(plan, state);
                }
                return new("Failed", 3);
            }
        }
    }

    /// <summary>Commits the next cursor before waiting; cancelling the display delay must not invalidate a durable restart.</summary>
    private async Task<OrchestrationOutcome> RequestRestartAsync(PreOobeExecutionPlan plan, JournalState state,
        int seconds, CancellationToken cancellationToken)
    {
        if (++state.RestartCount > MaximumRestarts) throw new InvalidDataException("Restart budget exceeded.");
        state.DeferredRestart = false;
        state.Status = "AwaitingRestart";
        journal.Write(state);
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
        if (!cleanup.Dispose(plan, state, true, boot)) state.Status = "Failed";
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
            progress.Report(new(actions, status ?? state.Status, isResuming, restartSecondsRemaining));
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
