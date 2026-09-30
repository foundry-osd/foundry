// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Utilities.Storage;
using Foundry.Utilities.Tests.IO;

namespace Foundry.Utilities.Tests.Storage;

public sealed class WindowsVolumeStorageTests
{
    [Fact]
    public void LocalVolume_ReturnsFilesystemAndUsableCapacity()
    {
        string path = Path.GetTempPath();
        var drive = new DriveInfo(Path.GetPathRoot(path)!);

        Assert.Equal(drive.DriveFormat, WindowsVolumeStorage.GetFileSystem(path));
        Assert.InRange(WindowsVolumeStorage.GetAvailableBytes(path), 0, drive.TotalSize);
    }

    [Fact]
    public void ExistingDirectory_IsUsedWithoutReducingItToTheVolumeRoot()
    {
        using var directory = new TemporaryDirectory();

        Assert.Equal(directory.Path + Path.DirectorySeparatorChar, WindowsVolumeStorage.GetExistingDirectory(directory.Path));
    }

    [Theory]
    [InlineData("future\\nested\\boot.iso", false)]
    [InlineData("boot.iso", true)]
    [InlineData("boot.iso\\future\\nested", true)]
    public void Destination_UsesTheNearestExistingDirectory(string relativePath, bool existingFile)
    {
        using var directory = new TemporaryDirectory();
        if (existingFile) File.WriteAllText(Path.Combine(directory.Path, "boot.iso"), "existing output");
        string path = Path.Combine(directory.Path, relativePath);

        Assert.Equal(directory.Path + Path.DirectorySeparatorChar, WindowsVolumeStorage.GetExistingDirectory(path));
        Assert.True(WindowsVolumeStorage.GetAvailableBytes(path) >= 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnavailableUncShare_PreservesNativeIoError(bool queryFileSystem, bool nestedDestination)
    {
        string share = @"\\localhost\FoundryMissingShare-" + Guid.NewGuid().ToString("N");
        string path = nestedDestination ? Path.Combine(share, "future", "boot.iso") : share;

        IOException error = Assert.Throws<IOException>(() =>
        {
            if (queryFileSystem) WindowsVolumeStorage.GetFileSystem(path);
            else WindowsVolumeStorage.GetAvailableBytes(path);
        });

        Assert.NotEqual(0, Assert.IsType<Win32Exception>(error.InnerException).NativeErrorCode);
    }
}
