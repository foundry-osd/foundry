// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Hardware;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class OfflineWindowsComputerNameServiceTests
{
    [Fact]
    public async Task Read_UsesOwnedMountAndReturnsNameOnlyAfterConfirmedUnload()
    {
        var runner = new OfflineRegistryCleanupTests.HiveRunner();
        string? readMount = null;
        var service = new OfflineWindowsComputerNameService(runner, NullLogger<OfflineWindowsComputerNameService>.Instance,
            () => ["owned-system-hive"], mount => { readMount = mount; return "OLD-PC"; });
        string? name = await service.TryGetOfflineComputerNameAsync(TestContext.Current.CancellationToken);
        Assert.Equal("OLD-PC", name);
        Assert.StartsWith(@"HKLM\FoundryOfflineSystem_", readMount);
        Assert.False(runner.Loaded);
    }

    [Fact]
    public async Task Read_UnloadFailurePreventsReturningNameOrTryingAnotherHive()
    {
        var runner = new OfflineRegistryCleanupTests.HiveRunner { UnloadFails = true };
        int reads = 0;
        var service = new OfflineWindowsComputerNameService(runner, NullLogger<OfflineWindowsComputerNameService>.Instance,
            () => ["first-owned-hive", "second-owned-hive"], _ => { reads++; return "OLD-PC"; });
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.TryGetOfflineComputerNameAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(error.Data["FoundryRecoveryDiagnostic"]);
        Assert.Equal(1, reads);
        Assert.Single(runner.Calls, call => call[0] == "LOAD");
    }

    [Fact]
    public async Task Read_MissingNameContinuesOnlyAfterCleanUnload()
    {
        var runner = new OfflineRegistryCleanupTests.HiveRunner();
        int reads = 0;
        var service = new OfflineWindowsComputerNameService(runner, NullLogger<OfflineWindowsComputerNameService>.Instance,
            () => ["first-owned-hive", "second-owned-hive"], _ => ++reads == 1 ? "" : "VALID-PC");
        Assert.Equal("VALID-PC", await service.TryGetOfflineComputerNameAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reads);
        Assert.Equal(2, runner.Calls.Count(call => call[0] == "UNLOAD"));
    }
}
