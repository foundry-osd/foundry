// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Registration;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class DeadlinePolicyTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StopAt_Is60SecondsBeforeDeadline()
    {
        Assert.Equal(Deadline.AddSeconds(-60), DeadlinePolicy.StopAt(Deadline));
    }

    [Fact]
    public void StopAt_IsNull_WithoutDeadline()
    {
        Assert.Null(DeadlinePolicy.StopAt(null));
    }

    [Fact]
    public void IsReached_IsTrue_ForPastDeadline()
    {
        Assert.True(DeadlinePolicy.IsReached(Deadline, Deadline.AddSeconds(1)));
        Assert.True(DeadlinePolicy.IsReached(Deadline, Deadline));
        Assert.False(DeadlinePolicy.IsReached(Deadline, Deadline.AddSeconds(-1)));
        Assert.False(DeadlinePolicy.IsReached(null, Deadline));
    }
}
