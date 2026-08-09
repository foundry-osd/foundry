// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Headless.XUnit;
using Foundry.Avalonia.Services.Threading;

namespace Foundry.Avalonia.Tests.Threading;

public sealed class AvaloniaUiTimerTests
{
    [AvaloniaFact]
    public async Task StartRaisesTicksAndStopDisablesTheTimer()
    {
        using IUiTimer timer = new AvaloniaUiTimerFactory().Create(TimeSpan.FromMilliseconds(50));
        var firstTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int tickCount = 0;
        timer.Tick += (_, _) =>
        {
            tickCount++;
            firstTick.TrySetResult();
        };

        timer.Start();

        Assert.True(timer.IsEnabled);
        await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(5));

        timer.Stop();
        int stoppedTickCount = tickCount;
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.False(timer.IsEnabled);
        Assert.Equal(stoppedTickCount, tickCount);
    }

    [AvaloniaFact]
    public async Task DisposeStopsTheTimerAndPreventsRestart()
    {
        IUiTimer timer = new AvaloniaUiTimerFactory().Create(TimeSpan.FromMilliseconds(50));
        int tickCount = 0;
        timer.Tick += (_, _) => tickCount++;
        timer.Start();

        timer.Dispose();
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.False(timer.IsEnabled);
        Assert.Equal(0, tickCount);
        Assert.Throws<ObjectDisposedException>(timer.Start);
    }

    [AvaloniaFact]
    public void FactoryCreatesIndependentTimersWithTheRequestedInterval()
    {
        var factory = new AvaloniaUiTimerFactory();

        using IUiTimer first = factory.Create(TimeSpan.FromMilliseconds(10));
        using IUiTimer second = factory.Create(TimeSpan.FromMilliseconds(20));

        Assert.IsType<AvaloniaUiTimer>(first);
        Assert.IsType<AvaloniaUiTimer>(second);
        Assert.NotSame(first, second);
    }
}
