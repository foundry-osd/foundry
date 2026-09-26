// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Utilities.Storage;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableUncShare_PreservesNativeIoError(bool queryFileSystem)
    {
        string share = @"\\localhost\FoundryMissingShare-" + Guid.NewGuid().ToString("N");

        IOException error = Assert.Throws<IOException>(() =>
        {
            if (queryFileSystem) WindowsVolumeStorage.GetFileSystem(share);
            else WindowsVolumeStorage.GetAvailableBytes(share);
        });

        Assert.NotEqual(0, Assert.IsType<Win32Exception>(error.InnerException).NativeErrorCode);
    }
}
