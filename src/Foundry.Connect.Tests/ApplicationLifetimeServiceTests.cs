// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;
using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Tests;

public sealed class ApplicationLifetimeServiceTests
{
    [Fact]
    public void TryExit_FirstWriterWins()
    {
        var codes = new List<int>();
        var service = new ApplicationLifetimeService(codes.Add);
        Assert.True(service.TryExit(ChildRuntimeExitCode.Success));
        Assert.False(service.TryExit(ChildRuntimeExitCode.UserAborted));
        Assert.Equal([0], codes);
        Assert.Equal(ChildRuntimeExitCode.Success, service.RequestedExitCode);
        Assert.True(service.ExitRequested.IsCancellationRequested);
    }

    [Fact]
    public void TryAcquire_SecondInstanceFails()
    {
        string name = $"Local\\Foundry.Connect.Test.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out SingleInstanceGuard? first));
        using (first)
        {
            Assert.False(SingleInstanceGuard.TryAcquire(name, out _));
        }
    }
}
