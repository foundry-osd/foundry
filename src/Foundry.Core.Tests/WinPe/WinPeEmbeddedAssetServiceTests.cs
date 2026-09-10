// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeEmbeddedAssetServiceTests
{



    [Fact]
    public void GetUsbProvisioningScriptTemplateContent_ReturnsProvisioningScriptTemplate()
    {
        var service = new WinPeEmbeddedAssetService();

        string content = service.GetUsbProvisioningScriptTemplateContent();

        Assert.Contains("Clear-Disk -Number $diskNumber", content, StringComparison.Ordinal);
        Assert.Contains("{{DISK_NUMBER}}", content, StringComparison.Ordinal);
        Assert.Contains("{{PARTITION_STYLE}}", content, StringComparison.Ordinal);
        Assert.Contains("AssignDriveLetter = $true", content, StringComparison.Ordinal);
        Assert.Contains("$bootDriveLetter = $bootPartition.DriveLetter", content, StringComparison.Ordinal);
        Assert.Contains("$cacheDriveLetter = $cachePartition.DriveLetter", content, StringComparison.Ordinal);
    }

    [Fact]
    public void GetIanaWindowsTimeZoneMapJson_ReturnsEmbeddedTimeZoneMap()
    {
        var service = new WinPeEmbeddedAssetService();

        string content = service.GetIanaWindowsTimeZoneMapJson();

        Assert.Contains("Europe/Paris", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Romance Standard Time", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSevenZipSourceDirectoryPath_ReturnsBundledAssets()
    {
        var service = new WinPeEmbeddedAssetService();

        string sourceDirectoryPath = service.GetSevenZipSourceDirectoryPath();

        Assert.True(Directory.Exists(sourceDirectoryPath), sourceDirectoryPath);
        Assert.True(File.Exists(Path.Combine(sourceDirectoryPath, "x64", "7za.exe")));
        Assert.True(File.Exists(Path.Combine(sourceDirectoryPath, "arm64", "7za.exe")));
        Assert.True(File.Exists(Path.Combine(sourceDirectoryPath, "License.txt")));
        Assert.True(File.Exists(Path.Combine(sourceDirectoryPath, "readme.txt")));
    }






}
