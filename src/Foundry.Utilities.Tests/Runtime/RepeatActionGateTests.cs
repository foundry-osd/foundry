// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Runtime;

namespace Foundry.Utilities.Tests.Runtime;

public sealed class RepeatActionGateTests
{
    [Fact]
    public void TryEnter_WithSameTargetInsideRepeatInterval_RejectsRepeatedAction()
    {
        var timeProvider = new ManualTimeProvider();
        var gate = new RepeatActionGate<object>(TimeSpan.FromMilliseconds(300), timeProvider);
        var target = new object();

        Assert.True(gate.TryEnter(target));

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        Assert.False(gate.TryEnter(target));
    }

    [Fact]
    public void TryEnter_WithSameTargetAfterRepeatInterval_AcceptsAction()
    {
        var timeProvider = new ManualTimeProvider();
        var gate = new RepeatActionGate<object>(TimeSpan.FromMilliseconds(300), timeProvider);
        var target = new object();

        Assert.True(gate.TryEnter(target));

        timeProvider.Advance(TimeSpan.FromMilliseconds(300));

        Assert.True(gate.TryEnter(target));
    }

    [Fact]
    public void TryEnter_WithDifferentTargetInsideRepeatInterval_AcceptsAction()
    {
        var timeProvider = new ManualTimeProvider();
        var gate = new RepeatActionGate<object>(TimeSpan.FromMilliseconds(300), timeProvider);

        Assert.True(gate.TryEnter(new object()));

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(gate.TryEnter(new object()));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration)
        {
            timestamp += duration.Ticks;
        }
    }
}
