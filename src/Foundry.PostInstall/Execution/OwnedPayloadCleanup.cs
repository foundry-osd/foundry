// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Windows;
using Serilog;

namespace Foundry.PostInstall.Execution;

public sealed record OwnedPayloadCleanupOutcome(bool HasFatalSensitiveFailure, bool HasDomainCleanupPending);

public sealed class OwnedPayloadCleanup(string root, ExecutionJournal journal, Action<TimeSpan>? retryDelay = null,
    Func<string, int>? scheduleDeletionAtRestart = null)
{
    public OwnedPayloadCleanupOutcome Dispose(PreOobeExecutionPlan plan, JournalState state, bool terminal, string boot)
    {
        PreOobePlanValidator.ValidatePlan(plan);
        string? domainPath = plan.Actions.Any(action => action.BuiltInKind == PreOobeBuiltInKind.DomainJoinAndPlacement)
            ? DomainJoinBinding.Validate(plan).Parameters.CredentialPayloadPath : null;
        bool fatal = false;
        bool domainPending = false;
        foreach (PreOobeOwnedPayload payload in plan.OwnedPayloads)
        {
            if (state.PayloadDispositions.GetValueOrDefault(payload.RelativePath) == "Disposed") continue;
            bool consumed = payload.ConsumerActionIds.Count > 0 && payload.ConsumerActionIds.All(id =>
                state.Actions.TryGetValue(id, out var result) && result.Status is "Succeeded" or "Failed" or "Skipped");
            if (!terminal && !(payload.IsSensitive && consumed)) continue;
            bool domain = payload.RelativePath == domainPath;
            bool unsafeExecution = state.UnsafePayloadBootIdentity == boot &&
                (state.UnsafeActionId is null || payload.ConsumerActionIds.Contains(state.UnsafeActionId));
            state.PayloadDispositions[payload.RelativePath] = "CleanupPending";
            bool disposed = false;
            try
            {
                // For a sensitive payload, a journal publication failure must propagate even if best-effort deletion succeeds.
                try { journal.Write(state); }
                finally { if (!unsafeExecution) disposed = TryDelete(payload); }
                if (disposed) state.PayloadDispositions[payload.RelativePath] = "Disposed";
                else
                {
                    fatal |= payload.IsSensitive && !domain;
                    domainPending |= domain;
                    state.HasWarnings = true;
                    if (domain && terminal && !payload.IsDirectory) ScheduleDeletionAtRestart(payload);
                }
                journal.Write(state);
            }
            catch (Exception error) when (!payload.IsSensitive && error is IOException or UnauthorizedAccessException)
            {
                state.HasWarnings = true;
            }
        }
        return new(fatal, domainPending);
    }

    /// <summary>
    /// The final pass is the last PostInstall run before OOBE: Setup never restarts the hook after it, so a credential
    /// file that is still locked would otherwise stay on disk. Session Manager deletes it during the next boot instead.
    /// </summary>
    private void ScheduleDeletionAtRestart(PreOobeOwnedPayload payload)
    {
        try
        {
            string path = OwnedPaths.Resolve(root, payload.RelativePath);
            int win32Error = (scheduleDeletionAtRestart ?? PendingFileDeletion.ScheduleAtRestart)(path);
            if (win32Error == 0)
                Log.Warning("Domain credential payload could not be deleted; it is scheduled for deletion at the next restart");
            else
                Log.Error("Domain credential payload could not be deleted or scheduled for deletion at the next restart; Win32 error {Win32Error}",
                    win32Error);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Error("Domain credential payload could not be scheduled for deletion at the next restart; failure type {FailureType}",
                error.GetType().Name);
        }
    }

    private bool TryDelete(PreOobeOwnedPayload payload)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                string path = OwnedPaths.Resolve(root, payload.RelativePath);
                if (payload.IsDirectory) OwnedPaths.DeleteDirectory(path);
                else if (File.Exists(path)) { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); File.Delete(path); }
                return true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                if (attempt < 2) (retryDelay ?? Thread.Sleep)(TimeSpan.FromMilliseconds(250));
            }
        }
        return false;
    }
}
