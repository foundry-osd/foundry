// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Utilities.Storage;

namespace Foundry.Utilities.Tests.Storage;

public sealed class WindowsVolumeDiscoveryTests
{
    [Fact]
    public void GetVolumes_WhenDriveIsReady_ReturnsAllAvailableFacts()
    {
        var discovery = new WindowsVolumeDiscovery(() =>
        [
            new FakeDriveInfo
            {
                RootPath = @"D:\",
                VolumeLabel = "Data",
                DriveType = DriveType.Fixed,
                IsReady = true,
                AvailableFreeSpace = 42
            }
        ]);

        VolumeInfo volume = Assert.Single(discovery.GetVolumes());

        Assert.Equal(@"D:\", volume.RootPath);
        Assert.Equal("Data", volume.VolumeLabel);
        Assert.Equal(DriveType.Fixed, volume.DriveType);
        Assert.True(volume.IsReady);
        Assert.Equal(42, volume.AvailableFreeSpace);
    }

    [Fact]
    public void GetVolumes_WhenDriveIsNotReady_ReturnsOnlySafeFacts()
    {
        var drive = new FakeDriveInfo
        {
            RootPath = @"E:\",
            DriveType = DriveType.CDRom,
            IsReady = false,
            ThrowWhenReadyOnlyFactsAreRead = true
        };
        var discovery = new WindowsVolumeDiscovery(() => [drive]);

        VolumeInfo volume = Assert.Single(discovery.GetVolumes());

        Assert.Equal(@"E:\", volume.RootPath);
        Assert.Equal(string.Empty, volume.VolumeLabel);
        Assert.Equal(DriveType.CDRom, volume.DriveType);
        Assert.False(volume.IsReady);
        Assert.Equal(0, volume.AvailableFreeSpace);
    }

    [Fact]
    public void GetVolumes_WhenReadyOnlyFactsAreInaccessible_ReturnsSafeFallbacks()
    {
        var inaccessibleDrive = new FakeDriveInfo
        {
            RootPath = @"E:\",
            DriveType = DriveType.Removable,
            IsReady = true,
            ThrowWhenReadyOnlyFactsAreRead = true
        };
        var discovery = new WindowsVolumeDiscovery(() => [inaccessibleDrive]);

        VolumeInfo volume = Assert.Single(discovery.GetVolumes());

        Assert.Equal(@"E:\", volume.RootPath);
        Assert.Equal(string.Empty, volume.VolumeLabel);
        Assert.Equal(DriveType.Removable, volume.DriveType);
        Assert.True(volume.IsReady);
        Assert.Equal(0, volume.AvailableFreeSpace);
    }

    [Fact]
    public void GetVolumes_WhenDriveEnumerationFailsUnexpectedly_PropagatesFailure()
    {
        var discovery = new WindowsVolumeDiscovery(
            () => throw new InvalidOperationException("Unexpected failure."));

        Assert.Throws<InvalidOperationException>(() => discovery.GetVolumes());
    }

    [Fact]
    public void GetVolumes_WhenDrivePropertyFailsUnexpectedly_PropagatesFailure()
    {
        var drive = new FakeDriveInfo
        {
            RootPath = @"E:\",
            DriveType = DriveType.Removable,
            IsReady = true,
            ReadyOnlyFactsException = new InvalidOperationException("Unexpected failure.")
        };
        var discovery = new WindowsVolumeDiscovery(() => [drive]);

        Assert.Throws<InvalidOperationException>(() => discovery.GetVolumes());
    }

    private sealed class FakeDriveInfo : IWindowsDriveInfo
    {
        private string _volumeLabel = string.Empty;
        private long _availableFreeSpace;

        public required string RootPath { get; init; }

        public string VolumeLabel
        {
            get => ReadyOnlyFactsException is not null
                ? throw ReadyOnlyFactsException
                : _volumeLabel;
            init => _volumeLabel = value;
        }

        public DriveType DriveType { get; init; }

        public bool IsReady { get; init; }

        public long AvailableFreeSpace
        {
            get => ReadyOnlyFactsException is not null
                ? throw ReadyOnlyFactsException
                : _availableFreeSpace;
            init => _availableFreeSpace = value;
        }

        public bool ThrowWhenReadyOnlyFactsAreRead
        {
            init => ReadyOnlyFactsException = value
                ? new IOException("The drive is inaccessible.")
                : null;
        }

        public Exception? ReadyOnlyFactsException { get; init; }
    }
}
