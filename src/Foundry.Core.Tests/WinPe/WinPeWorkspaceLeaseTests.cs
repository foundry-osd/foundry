// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeWorkspaceLeaseTests
{
    [Fact]
    public void Cleanup_WhenOperationIsLive_PreservesItAndAnotherLiveOperation()
    {
        using var temp = new TemporaryDirectory();
        using var first = WinPeWorkspaceLease.Create(temp.Path);
        using var second = WinPeWorkspaceLease.Create(temp.Path);
        var cleanup = new WinPeWorkspaceCleanupService(() => []);

        Assert.NotEqual(first.OperationDirectoryPath, second.OperationDirectoryPath);
        Assert.False(Directory.Exists(first.WinPeDirectoryPath)); // Copype must create this itself.
        Assert.False(cleanup.DeleteOwnedOperation(temp.Path, first.OperationDirectoryPath).IsSuccess);
        Assert.False(cleanup.DeleteOwnedOperation(temp.Path, second.OperationDirectoryPath).IsSuccess);
        Assert.True(Directory.Exists(first.OperationDirectoryPath));
        Assert.True(Directory.Exists(second.OperationDirectoryPath));
    }

    [Fact]
    public void Cleanup_WhenOperationIsInactive_DeletesPartialCopypeButPreservesUnknownDirectories()
    {
        using var temp = new TemporaryDirectory();
        string operation;
        using (var lease = WinPeWorkspaceLease.Create(temp.Path))
        {
            operation = lease.OperationDirectoryPath;
            Directory.CreateDirectory(lease.WinPeDirectoryPath);
            File.WriteAllText(Path.Combine(lease.WinPeDirectoryPath, "partial.wim"), "partial");
        }
        string unknown = Path.Combine(temp.Path, "legacy");
        Directory.CreateDirectory(unknown);
        var cleanup = new WinPeWorkspaceCleanupService(() => []);

        Assert.False(cleanup.DeleteOwnedOperation(temp.Path, unknown).IsSuccess);
        Assert.True(cleanup.DeleteOwnedOperation(temp.Path, operation).IsSuccess);
        Assert.False(Directory.Exists(operation));
        Assert.True(Directory.Exists(unknown));
    }

    [Fact]
    public void Cleanup_WhenOwnedOperationHasUncertainMount_PreservesIt()
    {
        using var temp = new TemporaryDirectory();
        string operation;
        using (var lease = WinPeWorkspaceLease.Create(temp.Path))
        {
            operation = lease.OperationDirectoryPath;
            Directory.CreateDirectory(lease.WinPeDirectoryPath);
            File.WriteAllText(Path.Combine(lease.WinPeDirectoryPath, ".foundry-mount-cleanup-test.pending"), "pending");
        }
        var cleanup = new WinPeWorkspaceCleanupService(() => []);

        Assert.False(cleanup.DeleteOwnedOperation(temp.Path, operation).IsSuccess);
        Assert.True(Directory.Exists(operation));
        Assert.False(cleanup.DeleteOwnedOperation(Path.Combine(temp.Path, "other"), operation).IsSuccess);
    }
}
