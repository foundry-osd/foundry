// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Foundry.Deploy.Services.Cache;

namespace Foundry.Deploy.Tests;

public sealed class VolumeStorageProbeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inspect_ReportsWritableCapacityAndRemovesProbe(bool useVolumeGuid)
    {
        string owned = Path.Combine(Path.GetTempPath(), "FoundryVolumeProbeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        try
        {
            string path = owned;
            if (useVolumeGuid)
            {
                var mount = new StringBuilder(1024);
                Assert.True(GetVolumePathNameW(owned, mount, mount.Capacity), new Win32Exception(Marshal.GetLastWin32Error()).Message);
                var volume = new StringBuilder(1024);
                Assert.True(GetVolumeNameForVolumeMountPointW(mount.ToString(), volume, volume.Capacity), new Win32Exception(Marshal.GetLastWin32Error()).Message);
                path = Path.Combine(volume.ToString(), Path.GetRelativePath(mount.ToString(), owned));
            }
            VolumeStorageStatus status = new VolumeStorageProbe().Inspect(Path.Combine(path, "payload"));
            Assert.True(status.IsPresent);
            Assert.True(status.IsWritable);
            Assert.True(status.FreeBytes > 0);
            Assert.Empty(Directory.GetFiles(owned, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(owned, recursive: true);
        }
    }

    [Fact]
    public void Inspect_MissingVolumeDoesNotReportUsableCapacity()
    {
        VolumeStorageStatus status = new VolumeStorageProbe().Inspect($@"\\?\Volume{{{Guid.NewGuid():D}}}\payload");
        Assert.False(status.IsPresent);
        Assert.False(status.IsWritable);
        Assert.Null(status.FreeBytes);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePathName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder volumeName, int bufferLength);
}
