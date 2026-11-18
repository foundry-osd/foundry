// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Ui;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_SecondInstanceFails()
    {
        string name = $"Local\\Foundry.WinPE.Ui.Test.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out SingleInstanceGuard? first));
        using (first)
        {
            Assert.False(SingleInstanceGuard.TryAcquire(name, out _));
        }
    }

    [Fact]
    public void Dispose_ReleasesInstanceName()
    {
        string name = $"Local\\Foundry.WinPE.Ui.Test.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out SingleInstanceGuard? first));
        first!.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(name, out SingleInstanceGuard? second));
        second!.Dispose();
    }
}
