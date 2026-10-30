// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Tests.Support;

internal static class AsyncAssert
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Polls every 10 ms for at most 5 s of real time; used only to observe continuations scheduled by a
    /// <see cref="ManualTimeProvider"/> advance.
    /// </summary>
    public static async Task EventuallyAsync(Func<bool> condition, string because)
    {
        ArgumentNullException.ThrowIfNull(condition);

        long start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > Limit.TotalMilliseconds)
            {
                Assert.Fail($"Condition was not met within {Limit.TotalSeconds:0} s: {because}");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }
}
