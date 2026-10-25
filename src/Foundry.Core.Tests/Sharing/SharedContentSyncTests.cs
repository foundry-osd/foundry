// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

public sealed class SharedContentSyncTests
{
    [Theory]
    [InlineData(SharingRole.None)]
    [InlineData(SharingRole.Reference)]
    public void Evaluate_NotAFollower_IsSynced(SharingRole role)
    {
        Assert.Equal(SharedContentSyncState.Synced, SharedContentSync.Evaluate(role, isArriving: true, failedCount: 1, isStalled: true));
    }

    [Theory]
    [InlineData(true, 0, false, SharedContentSyncState.Syncing)]
    [InlineData(true, 2, true, SharedContentSyncState.Syncing)]
    [InlineData(false, 2, true, SharedContentSyncState.Failed)]
    [InlineData(false, 0, true, SharedContentSyncState.Stalled)]
    [InlineData(false, 0, false, SharedContentSyncState.Synced)]
    public void Evaluate_Follower_ReportsArrivingThenFailedThenStalled(bool isArriving, int failedCount, bool isStalled, SharedContentSyncState expected)
    {
        Assert.Equal(expected, SharedContentSync.Evaluate(SharingRole.Follower, isArriving, failedCount, isStalled));
    }
}
