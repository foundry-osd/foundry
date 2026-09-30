// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeWorkspaceCleanupServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EnsureServicingCanStart_WhenOwnedOperationHasPendingCleanup_BlocksNewServicing(bool markerInOperationRoot, bool operationIsActive)
    {
        using var temp = new TemporaryDirectory();
        using var previous = WinPeWorkspaceLease.Create(temp.Path);
        Directory.CreateDirectory(previous.WinPeDirectoryPath);
        string marker = Path.Combine(markerInOperationRoot ? previous.OperationDirectoryPath : previous.WinPeDirectoryPath,
            ".foundry-mount-cleanup-test.pending");
        File.WriteAllText(marker, "execution not confirmed complete");
        if (!operationIsActive) previous.Dispose();
        var service = new WinPeWorkspaceCleanupService(() => []);

        WinPeResult result = service.EnsureServicingCanStart(temp.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.Equal([previous.OperationDirectoryPath], Directory.GetDirectories(temp.Path));
        Assert.Equal("execution not confirmed complete", File.ReadAllText(marker));
        Assert.True(File.Exists(Path.Combine(previous.OperationDirectoryPath, ".lease")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureServicingCanStart_WhenUnownedDirectoryContainsPendingMarker_PreservesItWithoutGrantingCleanup(bool directoryHasGuidName)
    {
        using var temp = new TemporaryDirectory();
        string unowned = Path.Combine(temp.Path, directoryHasGuidName ? Guid.NewGuid().ToString("N") : "legacy");
        string winPe = Path.Combine(unowned, "WinPe");
        Directory.CreateDirectory(winPe);
        string marker = Path.Combine(winPe, ".foundry-mount-cleanup-test.pending");
        File.WriteAllText(marker, "unowned state");
        var service = new WinPeWorkspaceCleanupService(() => []);

        WinPeResult result = service.EnsureServicingCanStart(temp.Path);
        WinPeResult cleanup = service.DeleteOwnedOperation(temp.Path, unowned);

        Assert.True(result.IsSuccess);
        Assert.False(cleanup.IsSuccess);
        Assert.Equal("unowned state", File.ReadAllText(marker));
    }

    [Fact]
    public void EnsureServicingCanStart_WhenOwnedPreviousOperationHasNoCleanupMarker_AllowsNewServicing()
    {
        using var temp = new TemporaryDirectory();
        using var previous = WinPeWorkspaceLease.Create(temp.Path);
        Directory.CreateDirectory(previous.WinPeDirectoryPath);
        var service = new WinPeWorkspaceCleanupService(() => []);

        WinPeResult result = service.EnsureServicingCanStart(temp.Path);

        Assert.True(result.IsSuccess);
        Assert.Equal([previous.OperationDirectoryPath], Directory.GetDirectories(temp.Path));
    }

    [Fact]
    public void EnsureServicingCanStart_WhenOwnedOperationStateCannotBeRead_BlocksNewServicing()
    {
        using var temp = new TemporaryDirectory();
        using var previous = WinPeWorkspaceLease.Create(temp.Path);
        File.WriteAllText(Path.Combine(previous.OperationDirectoryPath, "operation.json"), "{invalid");
        var service = new WinPeWorkspaceCleanupService(() => []);

        WinPeResult result = service.EnsureServicingCanStart(temp.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.True(Directory.Exists(previous.OperationDirectoryPath));
    }

    [Theory]
    [InlineData(@"\\?\")]
    [InlineData(@"\\.\")]
    [InlineData("//?/")]
    [InlineData("//./")]
    public void Delete_WhenInventoryUsesDevicePath_PreservesWorkspace(string prefix)
    {
        using var temp = new TemporaryDirectory();
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        var service = new WinPeWorkspaceCleanupService(() =>
            [new WinPeMountedImage(prefix + Path.Combine(workspace, "mount"), prefix + Path.Combine(workspace, "boot.wim"))]);

        Assert.False(service.Delete(workspace).IsSuccess);
        Assert.True(Directory.Exists(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_WhenCleanupExecutionIsUnresolved_PreservesWorkspaceEvenWithoutRegisteredMounts(bool markerInParent)
    {
        using var temp = new TemporaryDirectory();
        string workspace = Path.Combine(temp.Path, "workspace");
        string nested = Path.Combine(workspace, "source");
        Directory.CreateDirectory(nested);
        string marker = Path.Combine(workspace, ".foundry-mount-cleanup-test.pending");
        File.WriteAllText(marker, "execution not confirmed complete");
        var service = new WinPeWorkspaceCleanupService(() => []);

        Assert.False(service.Delete(markerInParent ? nested : temp.Path).IsSuccess);
        Assert.True(File.Exists(marker));
        Assert.True(Directory.Exists(nested));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_WhenTargetIsMissingAndAncestorCleanupIsUnresolved_BlocksNewSource(bool intermediateDirectoryMissing)
    {
        using var temp = new TemporaryDirectory();
        string source = intermediateDirectoryMissing
            ? Path.Combine(temp.Path, "missing", "windows-source-Enterprise")
            : Path.Combine(temp.Path, "windows-source-Enterprise");
        string marker = Path.Combine(temp.Path, ".foundry-mount-cleanup-test.pending");
        File.WriteAllText(marker, "execution not confirmed complete");
        var service = new WinPeWorkspaceCleanupService(() => []);

        WinPeResult result = service.Delete(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.Equal("execution not confirmed complete", File.ReadAllText(marker));
        Assert.False(Directory.Exists(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_WhenTargetIsMissingWithoutCleanupMarkers_Succeeds(bool intermediateDirectoryMissing)
    {
        using var temp = new TemporaryDirectory();
        string source = intermediateDirectoryMissing
            ? Path.Combine(temp.Path, "missing", "windows-source-Enterprise")
            : Path.Combine(temp.Path, "windows-source-Enterprise");
        var service = new WinPeWorkspaceCleanupService(() => []);

        WinPeResult result = service.Delete(source);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(source));
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("same")]
    [InlineData("ancestor")]
    [InlineData("backing-image")]
    public void Delete_WhenMountOverlapsWorkspace_PreservesFilesAndAttributes(string location)
    {
        using var temp = new TemporaryDirectory();
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        string file = Path.Combine(workspace, "boot.wim");
        File.WriteAllText(file, "preserve");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        string mount = location switch
        {
            "nested" => Path.Combine(workspace, "windows-source-Pro", "install-mount"),
            "same" => workspace,
            "ancestor" => temp.Path,
            _ => Path.Combine(temp.Path, "outside")
        };
        string image = location == "backing-image" ? file : Path.Combine(temp.Path, "outside.wim");
        var service = new WinPeWorkspaceCleanupService(() => [new WinPeMountedImage(mount, image)]);
        try
        {
            WinPeResult result = service.Delete(workspace);

            Assert.False(result.IsSuccess);
            Assert.Equal("preserve", File.ReadAllText(file));
            Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
        }
        finally { File.SetAttributes(file, FileAttributes.Normal); }
    }

    [Fact]
    public void Delete_WhenInventoryFails_PreservesWorkspace()
    {
        using var temp = new TemporaryDirectory();
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "keep.txt"), "keep");
        var service = new WinPeWorkspaceCleanupService(() => throw new IOException("Inventory unavailable."));

        WinPeResult result = service.Delete(workspace);

        Assert.False(result.IsSuccess);
        Assert.Contains("Inventory unavailable", result.Error?.Details, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(workspace, "keep.txt")));
    }

    [Fact]
    public void Delete_WhenOnlySiblingMountExists_CleansReadOnlyWorkspace()
    {
        using var temp = new TemporaryDirectory();
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "logs"));
        string file = Path.Combine(workspace, "logs", "readonly.txt");
        File.WriteAllText(file, "delete");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var service = new WinPeWorkspaceCleanupService(() =>
            [new WinPeMountedImage(workspace + "-other", Path.Combine(temp.Path, "other.wim"))]);

        Assert.True(service.Delete(workspace).IsSuccess);
        Assert.False(Directory.Exists(workspace));
    }

    [Fact]
    public void Delete_WhenMountWasRemoved_RechecksInventoryBeforeDeleting()
    {
        using var temp = new TemporaryDirectory();
        string workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(workspace);
        bool mounted = true;
        var service = new WinPeWorkspaceCleanupService(() => mounted
            ? [new WinPeMountedImage(Path.Combine(workspace, "mount"), Path.Combine(workspace, "boot.wim"))] : []);

        Assert.False(service.Delete(workspace).IsSuccess);
        mounted = false;
        Assert.True(service.Delete(workspace).IsSuccess);
        Assert.False(Directory.Exists(workspace));
    }

    [Fact]
    public void Delete_WhenLooseFileBacksMountedImage_PreservesIt()
    {
        using var temp = new TemporaryDirectory();
        string image = Path.Combine(temp.Path, "boot.wim");
        File.WriteAllText(image, "image");
        var service = new WinPeWorkspaceCleanupService(() =>
            [new WinPeMountedImage(Path.Combine(temp.Path, "elsewhere"), image)]);

        Assert.False(service.Delete(image).IsSuccess);
        Assert.Equal("image", File.ReadAllText(image));
    }

    [Fact]
    public void Delete_WhenNativePathIsInvalid_DoesNotAssumeNoMounts()
    {
        using var temp = new TemporaryDirectory();
        var service = new WinPeWorkspaceCleanupService(() => [new WinPeMountedImage("relative", "invalid")]);

        Assert.False(service.Delete(temp.Path).IsSuccess);
        Assert.True(Directory.Exists(temp.Path));
    }
}
