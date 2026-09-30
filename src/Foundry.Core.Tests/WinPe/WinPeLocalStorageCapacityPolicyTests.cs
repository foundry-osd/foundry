// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeLocalStorageCapacityPolicyTests
{
    [Theory]
    [InlineData(0L, false)]
    [InlineData(21_474_836_479L, false)]
    [InlineData(21_474_836_480L, true)]
    [InlineData(21_474_836_481L, true)]
    public void Validate_RequiresTwentyGiBOfAvailableSpace(long available, bool succeeds)
    {
        WinPeResult result = WinPeLocalStorageCapacityPolicy.Validate([@"C:\Foundry\Workspaces"], _ => available, TestContext.Current.CancellationToken);

        Assert.Equal(succeeds, result.IsSuccess);
        if (!succeeds)
        {
            Assert.Equal("WINPE_LOCAL_SPACE_INSUFFICIENT", result.Error?.Code);
            Assert.Equal(21_474_836_480UL, result.Error?.RequiredBytes);
            Assert.Equal((ulong)available, result.Error?.AvailableBytes);
        }
    }

    [Fact]
    public void Validate_ChecksEveryDestinationInsteadOfAssumingTheWorkspaceDrive()
    {
        WinPeResult result = WinPeLocalStorageCapacityPolicy.Validate([@"C:\Foundry", @"D:\Cache"],
            path => path.StartsWith("C:", StringComparison.Ordinal) ? 40L * 1024 * 1024 * 1024 : 1024, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(1024UL, result.Error?.AvailableBytes);
        Assert.Contains(@"D:\Cache", result.Error?.Details);
        Assert.Equal(@"D:\Cache", result.Error?.StoragePath);
    }

    [Fact]
    public void Validate_WhenCapacityCannotBeRead_PreservesTheCauseAndBlocksCreation()
    {
        var failure = new IOException("Volume unavailable.");
        WinPeResult result = WinPeLocalStorageCapacityPolicy.Validate([@"D:\Cache"], _ => throw failure, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("WINPE_LOCAL_SPACE_UNKNOWN", result.Error?.Code);
        Assert.Same(failure, result.Error?.Exception);
        Assert.Contains(@"D:\Cache", result.Error?.Details);
        Assert.Equal(@"D:\Cache", result.Error?.StoragePath);
    }

    [Fact]
    public void Validate_WhenCancelled_DoesNotQueryCapacity()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => WinPeLocalStorageCapacityPolicy.Validate(
            [@"C:\Foundry"], _ => throw new InvalidOperationException("Capacity must not be queried."), cancellation.Token));
    }

    [Fact]
    public void Validate_WhenCancelledDuringTheFinalQuery_DoesNotAdmitCreation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Assert.ThrowsAny<OperationCanceledException>(() => WinPeLocalStorageCapacityPolicy.Validate([@"C:\Foundry"], _ =>
        {
            cancellation.Cancel();
            return 40L * 1024 * 1024 * 1024;
        }, cancellation.Token));
    }
}
