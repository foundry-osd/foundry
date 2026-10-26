// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Sharing;

/// <summary>Whether the library content of the publication a follower applied is complete on this PC.</summary>
public enum SharedContentSyncState
{
    /// <summary>Not a follower, or every item of the applied publication is in the local libraries.</summary>
    Synced,

    /// <summary>The content is still downloading or installing, or waits for the next check.</summary>
    Syncing,

    /// <summary>Some content could not be imported; the sharing card names it.</summary>
    Failed,

    /// <summary>The same content stayed unavailable for many checks; it is probably damaged in the folder.</summary>
    Stalled
}

/// <summary>Decides the <see cref="SharedContentSyncState"/> that gates media creation on a follower.</summary>
public static class SharedContentSync
{
    /// <summary>
    /// Returns the sync state of a PC: <see cref="SharedContentSyncState.Syncing"/> while the content is arriving (a running fetch
    /// also covers a retry after a failure), then <see cref="SharedContentSyncState.Failed"/>, then
    /// <see cref="SharedContentSyncState.Stalled"/>; a PC that does not follow is always synced.
    /// </summary>
    /// <param name="role">Sharing role of the PC.</param>
    /// <param name="isArriving">The content of the applied publication is still on its way (<see cref="ConfigurationSharingEngine.IsContentArriving"/>).</param>
    /// <param name="failedCount">Items whose import failed (<see cref="ConfigurationSharingEngine.FailedContent"/>).</param>
    /// <param name="isStalled">The content stayed unavailable for many checks (<see cref="ConfigurationSharingEngine.IsContentStalled"/>).</param>
    public static SharedContentSyncState Evaluate(SharingRole role, bool isArriving, int failedCount, bool isStalled) =>
        role != SharingRole.Follower ? SharedContentSyncState.Synced
        : isArriving ? SharedContentSyncState.Syncing
        : failedCount > 0 ? SharedContentSyncState.Failed
        : isStalled ? SharedContentSyncState.Stalled
        : SharedContentSyncState.Synced;
}
