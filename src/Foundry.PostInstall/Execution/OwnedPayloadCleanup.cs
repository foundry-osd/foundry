// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;

namespace Foundry.PostInstall.Execution;

public sealed class OwnedPayloadCleanup(string root, ExecutionJournal journal)
{
    public bool Dispose(PreOobeExecutionPlan plan, JournalState state, bool terminal, string boot)
    {
        bool sensitiveFailure = false;
        foreach (PreOobeOwnedPayload payload in plan.OwnedPayloads)
        {
            if (state.PayloadDispositions.GetValueOrDefault(payload.RelativePath) == "Disposed") continue;
            bool consumed = payload.ConsumerActionIds.Count > 0 && payload.ConsumerActionIds.All(id =>
                state.Actions.TryGetValue(id, out var result) && result.Status is "Succeeded" or "Failed" or "Skipped");
            if (!terminal && !(payload.IsSensitive && consumed)) continue;
            if (state.UnsafePayloadBootIdentity == boot &&
                (state.UnsafeActionId is null || payload.ConsumerActionIds.Contains(state.UnsafeActionId)))
            {
                state.PayloadDispositions[payload.RelativePath] = "CleanupPending";
                sensitiveFailure |= payload.IsSensitive;
                continue;
            }
            state.PayloadDispositions[payload.RelativePath] = "CleanupPending";
            try
            {
                try { journal.Write(state); }
                finally
                {
                    string path = OwnedPaths.Resolve(root, payload.RelativePath);
                    if (payload.IsDirectory) OwnedPaths.DeleteDirectory(path);
                    else if (File.Exists(path)) { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); File.Delete(path); }
                }
                state.PayloadDispositions[payload.RelativePath] = "Disposed";
                journal.Write(state);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                sensitiveFailure |= payload.IsSensitive;
                state.HasWarnings = true;
            }
        }
        return !sensitiveFailure;
    }
}
