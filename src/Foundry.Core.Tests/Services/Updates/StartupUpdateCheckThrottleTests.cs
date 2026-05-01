using Foundry.Core.Services.Updates;

namespace Foundry.Core.Tests.Services.Updates;

public sealed class StartupUpdateCheckThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 17, 30, 0, TimeSpan.FromHours(2));
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    [Fact]
    public void ShouldRun_WhenNoPreviousCheck_ReturnsTrue()
    {
        Assert.True(StartupUpdateCheckThrottle.ShouldRun(lastCheckedAt: null, Now, Interval));
    }

    [Fact]
    public void ShouldRun_WhenPreviousCheckIsRecent_ReturnsFalse()
    {
        DateTimeOffset lastCheckedAt = Now.AddHours(-2);

        Assert.False(StartupUpdateCheckThrottle.ShouldRun(lastCheckedAt, Now, Interval));
    }

    [Fact]
    public void ShouldRun_WhenPreviousCheckReachedInterval_ReturnsTrue()
    {
        DateTimeOffset lastCheckedAt = Now.Subtract(Interval);

        Assert.True(StartupUpdateCheckThrottle.ShouldRun(lastCheckedAt, Now, Interval));
    }

    [Fact]
    public void ShouldRun_WhenPreviousCheckIsInFuture_ReturnsTrue()
    {
        DateTimeOffset lastCheckedAt = Now.AddMinutes(5);

        Assert.True(StartupUpdateCheckThrottle.ShouldRun(lastCheckedAt, Now, Interval));
    }

    [Fact]
    public void GetNextCheckAt_WhenPreviousCheckExists_ReturnsPreviousCheckPlusInterval()
    {
        DateTimeOffset lastCheckedAt = Now.AddHours(-2);

        DateTimeOffset nextCheckAt = StartupUpdateCheckThrottle.GetNextCheckAt(lastCheckedAt, Interval);

        Assert.Equal(lastCheckedAt.Add(Interval), nextCheckAt);
    }
}
