// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Storage;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class SupportBundleDestinationPolicyTests
{
    [Fact]
    public void SelectExternalDestination_PrefersFoundryCacheVolume()
    {
        VolumeInfo[] volumes =
        [
            CreateVolume(@"E:\", "USB", DriveType.Removable),
            CreateVolume(@"F:\", "Foundry Cache", DriveType.Fixed)
        ];

        string? destination = SupportBundleDestinationPolicy.SelectExternalDestination(volumes);

        Assert.Equal(@"F:\", destination);
    }

    [Fact]
    public void SelectExternalDestination_UsesReadyRemovableVolumeAsFallback()
    {
        VolumeInfo[] volumes =
        [
            CreateVolume(@"C:\", "Windows", DriveType.Fixed),
            CreateVolume(@"E:\", "USB", DriveType.Removable)
        ];

        string? destination = SupportBundleDestinationPolicy.SelectExternalDestination(volumes);

        Assert.Equal(@"E:\", destination);
    }

    [Fact]
    public void SelectExternalDestination_ExcludesWinPeAndUnavailableVolumes()
    {
        VolumeInfo[] volumes =
        [
            CreateVolume(@"X:\", "Foundry Cache", DriveType.Removable),
            CreateVolume(@"E:\", "USB", DriveType.Removable, isReady: false),
            CreateVolume(@"C:\", "Windows", DriveType.Fixed)
        ];

        string? destination = SupportBundleDestinationPolicy.SelectExternalDestination(volumes);

        Assert.Null(destination);
    }

    private static VolumeInfo CreateVolume(
        string rootPath,
        string volumeLabel,
        DriveType driveType,
        bool isReady = true)
    {
        return new VolumeInfo(rootPath, volumeLabel, driveType, isReady, AvailableFreeSpace: 1024);
    }
}
