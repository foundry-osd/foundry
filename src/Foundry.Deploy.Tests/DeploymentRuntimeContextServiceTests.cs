// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Runtime;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentRuntimeContextServiceTests
{
    [Fact]
    public void Resolve_WhenEnvironmentForcesIso_IgnoresDetectedUsbVolume()
    {
        var service = CreateService("iso", CreateVolume(@"F:\", "Foundry Cache"));

        DeploymentRuntimeContext result = service.Resolve();

        Assert.Equal(DeploymentMode.Iso, result.Mode);
        Assert.Null(result.UsbCacheRuntimeRoot);
    }

    [Fact]
    public void Resolve_WhenEnvironmentForcesUsb_UsesDetectedRuntimeRoot()
    {
        var service = CreateService(" USB ", CreateVolume(@"F:\", "fOuNdRy CaChE"));

        DeploymentRuntimeContext result = service.Resolve();

        Assert.Equal(DeploymentMode.Usb, result.Mode);
        Assert.Equal(@"F:\Runtime", result.UsbCacheRuntimeRoot);
    }

    [Fact]
    public void Resolve_WhenEnvironmentIsMissingAndCacheVolumeExists_DetectsUsbMode()
    {
        var service = CreateService(null, CreateVolume(@"G:\", "Foundry Cache"));

        DeploymentRuntimeContext result = service.Resolve();

        Assert.Equal(DeploymentMode.Usb, result.Mode);
        Assert.Equal(@"G:\Runtime", result.UsbCacheRuntimeRoot);
    }

    [Fact]
    public void Resolve_WhenEnvironmentIsMissingAndCacheVolumeDoesNotExist_FallsBackToIsoMode()
    {
        var service = CreateService(null, CreateVolume(@"C:\", "Windows"));

        DeploymentRuntimeContext result = service.Resolve();

        Assert.Equal(DeploymentMode.Iso, result.Mode);
        Assert.Null(result.UsbCacheRuntimeRoot);
    }

    private static DeploymentRuntimeContextService CreateService(string? deploymentMode, params VolumeInfo[] volumes)
    {
        return new DeploymentRuntimeContextService(
            new StubVolumeDiscovery(volumes),
            variableName => variableName == "FOUNDRY_DEPLOYMENT_MODE" ? deploymentMode : null);
    }

    private static VolumeInfo CreateVolume(string rootPath, string label)
    {
        return new VolumeInfo(rootPath, label, DriveType.Fixed, true, 100);
    }

    private sealed class StubVolumeDiscovery(IReadOnlyList<VolumeInfo> volumes) : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => volumes;
    }
}
