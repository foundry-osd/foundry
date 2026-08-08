// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class CacheLocatorServiceTests
{
    [Fact]
    public async Task ResolveAsync_WhenIsoModeAndPreferredPathMissing_UsesIsoPolicyRoot()
    {
        var service = CreateService();

        CacheResolution result = await service.ResolveAsync(
            DeploymentMode.Iso,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(@"X:\Foundry\Runtime", result.RootPath);
        Assert.Equal("ISO policy root", result.Source);
    }

    [Fact]
    public async Task ResolveAsync_WhenUsbModeAndPreferredPathIsExplicit_UsesPreferredPath()
    {
        var service = CreateService();

        CacheResolution result = await service.ResolveAsync(
            DeploymentMode.Usb,
            @" C:\CacheRoot ",
            TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\CacheRoot", result.RootPath);
        Assert.Equal("Preferred path", result.Source);
    }

    [Fact]
    public async Task ResolveAsync_WhenUsbModeUsesTransientPlaceholder_FindsCacheLabelIgnoringCase()
    {
        var service = CreateService(new VolumeInfo(@"F:\", "fOuNdRy CaChE", DriveType.Removable, true, 100));

        CacheResolution result = await service.ResolveAsync(
            DeploymentMode.Usb,
            @"X:\Foundry\Runtime",
            TestContext.Current.CancellationToken);

        Assert.Equal(@"F:\Runtime", result.RootPath);
        Assert.Equal("Detected USB cache partition", result.Source);
    }

    private static CacheLocatorService CreateService(params VolumeInfo[] volumes)
    {
        return new CacheLocatorService(
            NullLogger<CacheLocatorService>.Instance,
            new StubVolumeDiscovery(volumes));
    }

    private sealed class StubVolumeDiscovery(IReadOnlyList<VolumeInfo> volumes) : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => volumes;
    }
}
