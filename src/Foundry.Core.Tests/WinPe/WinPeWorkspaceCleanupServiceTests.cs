// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeWorkspaceCleanupServiceTests
{
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
