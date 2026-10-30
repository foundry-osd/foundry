// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkRefreshSignalTests
{
    [Fact]
    public async Task Request_CoalescesIntoOneWake()
    {
        using var signal = new NetworkRefreshSignal();

        signal.Request();
        signal.Request();
        await signal.WaitAsync(TestContext.Current.CancellationToken);
        Task second = signal.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(second.IsCompleted);
        signal.Request();
        await second;
    }
}
