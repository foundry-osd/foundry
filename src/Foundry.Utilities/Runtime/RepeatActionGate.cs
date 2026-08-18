// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Runtime;

public sealed class RepeatActionGate<T>
    where T : notnull
{
    private readonly TimeSpan repeatInterval;
    private readonly TimeProvider timeProvider;
    private readonly IEqualityComparer<T> comparer;
    private T? lastTarget;
    private long lastAcceptedTimestamp;
    private bool hasAcceptedAction;

    public RepeatActionGate(
        TimeSpan repeatInterval,
        TimeProvider? timeProvider = null,
        IEqualityComparer<T>? comparer = null)
    {
        if (repeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatInterval));
        }

        this.repeatInterval = repeatInterval;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public bool TryEnter(T target)
    {
        ArgumentNullException.ThrowIfNull(target);

        long currentTimestamp = timeProvider.GetTimestamp();
        if (hasAcceptedAction
            && comparer.Equals(lastTarget!, target)
            && timeProvider.GetElapsedTime(lastAcceptedTimestamp, currentTimestamp) < repeatInterval)
        {
            return false;
        }

        lastTarget = target;
        lastAcceptedTimestamp = currentTimestamp;
        hasAcceptedAction = true;
        return true;
    }
}
