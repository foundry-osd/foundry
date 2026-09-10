// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.SystemPreparation;
using Xunit;

namespace Foundry.Bootstrap.Tests.SystemPreparation;

public sealed class WindowsSystemPreparationPlatformTests
{
    [Fact]
    public void NativeTimeZoneLookup_PreservesWindowsKeyAndDaylightSavingRules()
    {
        var information = WindowsTimeZone.Find("romance standard time", TestContext.Current.CancellationToken);

        Assert.Equal("Romance Standard Time", information.TimeZoneKeyName);
        Assert.Equal(-60, information.Bias);
        Assert.Equal(-60, information.DaylightBias);
        Assert.Equal(3, information.DaylightDate.Month);
        Assert.Equal(10, information.StandardDate.Month);
    }

    [Fact]
    public void UnknownTimeZoneFailsBeforeChangingSystemSettings()
    {
        Assert.Throws<TimeZoneNotFoundException>(() => WindowsTimeZone.Set("Foundry-Missing-TimeZone", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CancelledTimeZoneChangeDoesNotApplySystemSettings()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => WindowsTimeZone.Set("Romance Standard Time", new CancellationToken(true)));
    }

    [Fact]
    public void IsValidWindowsTimeZone_RejectsIanaIdAndAcceptsWindowsId()
    {
        var platform = new WindowsSystemPreparationPlatform();

        Assert.False(platform.IsValidWindowsTimeZone("Europe/Paris"));
        Assert.True(platform.IsValidWindowsTimeZone("Romance Standard Time"));
    }

    [Fact]
    public async Task EnsureServiceRunningAsync_PollsUntilServiceReportsRunning()
    {
        var source = new RecordingServiceStatusSource(false, true);
        var manager = new WindowsServiceManager(source, TimeSpan.FromSeconds(10), TimeSpan.Zero);

        await manager.EnsureRunningAsync("dot3svc", CancellationToken.None);

        Assert.Equal("dot3svc", source.StartedService);
        Assert.Equal(2, source.QueryCount);
    }

    [Fact]
    public async Task EnsureServiceRunningAsync_TimesOutWhenServiceNeverReportsRunning()
    {
        var source = new RecordingServiceStatusSource(false);
        var manager = new WindowsServiceManager(source, TimeSpan.Zero, TimeSpan.Zero);

        await Assert.ThrowsAsync<TimeoutException>(
            () => manager.EnsureRunningAsync("dot3svc", CancellationToken.None));
    }

    private sealed class RecordingServiceStatusSource(params bool[] states) : IWindowsServiceStatusSource
    {
        private readonly Queue<bool> _states = new(states);
        public string? StartedService { get; private set; }
        public int QueryCount { get; private set; }

        public void Start(string serviceName)
        {
            StartedService = serviceName;
        }

        public bool IsRunning(string serviceName)
        {
            QueryCount++;
            return _states.Count > 1 ? _states.Dequeue() : _states.Peek();
        }
    }
}
