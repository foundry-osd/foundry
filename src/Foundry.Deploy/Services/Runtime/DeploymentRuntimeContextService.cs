// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Services.Runtime;

public sealed class DeploymentRuntimeContextService : IDeploymentRuntimeContextService
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";
    private const string CacheVolumeLabel = "Foundry Cache";
    private const string RuntimeFolderName = "Runtime";
    private readonly IVolumeDiscovery _volumeDiscovery;
    private readonly Func<string, string?> _environmentVariableReader;

    public DeploymentRuntimeContextService()
        : this(new WindowsVolumeDiscovery())
    {
    }

    public DeploymentRuntimeContextService(IVolumeDiscovery volumeDiscovery)
        : this(volumeDiscovery, Environment.GetEnvironmentVariable)
    {
    }

    internal DeploymentRuntimeContextService(
        IVolumeDiscovery volumeDiscovery,
        Func<string, string?> environmentVariableReader)
    {
        _volumeDiscovery = volumeDiscovery;
        _environmentVariableReader = environmentVariableReader;
    }

    public DeploymentRuntimeContext Resolve()
    {
        if (TryResolveDeploymentModeFromEnvironment(out DeploymentMode modeFromEnvironment))
        {
            string? usbRoot = modeFromEnvironment == DeploymentMode.Usb
                ? TryGetUsbCacheRuntimeRoot()
                : null;
            return new DeploymentRuntimeContext(modeFromEnvironment, usbRoot);
        }

        string? detectedUsbRoot = TryGetUsbCacheRuntimeRoot();
        return string.IsNullOrWhiteSpace(detectedUsbRoot)
            ? new DeploymentRuntimeContext(DeploymentMode.Iso, null)
            : new DeploymentRuntimeContext(DeploymentMode.Usb, detectedUsbRoot);
    }

    private bool TryResolveDeploymentModeFromEnvironment(out DeploymentMode mode)
    {
        string? raw = _environmentVariableReader(DeploymentModeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            mode = default;
            return false;
        }

        string normalized = raw.Trim().ToLowerInvariant();
        mode = normalized switch
        {
            "usb" => DeploymentMode.Usb,
            "iso" => DeploymentMode.Iso,
            _ => default
        };

        return normalized is "usb" or "iso";
    }

    private string? TryGetUsbCacheRuntimeRoot()
    {
        foreach (VolumeInfo volume in _volumeDiscovery.GetVolumes())
        {
            if (!volume.IsReady)
            {
                continue;
            }

            if (string.Equals(volume.VolumeLabel, CacheVolumeLabel, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(volume.RootPath, RuntimeFolderName);
            }
        }

        return null;
    }
}
