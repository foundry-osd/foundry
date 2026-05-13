using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryDailyActivityGateTests
{
    [Fact]
    public void ShouldTrack_WhenNoDateWasPersisted_ReturnsTrue()
    {
        Assert.True(TelemetryDailyActivityGate.ShouldTrack(DateOnly.FromDateTime(new DateTime(2026, 5, 14)), null));
    }

    [Fact]
    public void ShouldTrack_WhenPersistedDateIsToday_ReturnsFalse()
    {
        DateOnly today = DateOnly.FromDateTime(new DateTime(2026, 5, 14));

        Assert.False(TelemetryDailyActivityGate.ShouldTrack(today, "2026-05-14"));
    }

    [Fact]
    public void ShouldTrack_WhenPersistedDateIsOlder_ReturnsTrue()
    {
        DateOnly today = DateOnly.FromDateTime(new DateTime(2026, 5, 14));

        Assert.True(TelemetryDailyActivityGate.ShouldTrack(today, "2026-05-13"));
    }

    [Fact]
    public void ShouldTrack_WhenPersistedDateIsInvalid_ReturnsTrue()
    {
        DateOnly today = DateOnly.FromDateTime(new DateTime(2026, 5, 14));

        Assert.True(TelemetryDailyActivityGate.ShouldTrack(today, "invalid"));
    }

    [Fact]
    public void FormatDate_ReturnsIsoLocalDate()
    {
        DateOnly today = DateOnly.FromDateTime(new DateTime(2026, 5, 14));

        Assert.Equal("2026-05-14", TelemetryDailyActivityGate.FormatDate(today));
    }
}
