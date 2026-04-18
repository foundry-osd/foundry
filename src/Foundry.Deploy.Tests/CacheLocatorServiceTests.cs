using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class CacheLocatorServiceTests
{
    [Fact]
    public async Task ResolveAsync_WhenIsoModeAndPreferredPathMissing_UsesIsoPolicyRoot()
    {
        var service = new CacheLocatorService(NullLogger<CacheLocatorService>.Instance);

        CacheResolution result = await service.ResolveAsync(DeploymentMode.Iso);

        Assert.Equal(@"X:\Foundry\Runtime", result.RootPath);
        Assert.Equal("ISO policy root", result.Source);
    }

    [Fact]
    public async Task ResolveAsync_WhenUsbModeAndPreferredPathIsExplicit_UsesPreferredPath()
    {
        var service = new CacheLocatorService(NullLogger<CacheLocatorService>.Instance);

        CacheResolution result = await service.ResolveAsync(DeploymentMode.Usb, @" C:\CacheRoot ");

        Assert.Equal(@"C:\CacheRoot", result.RootPath);
        Assert.Equal("Preferred path", result.Source);
    }
}
