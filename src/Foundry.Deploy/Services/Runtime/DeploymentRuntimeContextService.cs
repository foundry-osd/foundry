// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Utilities.Storage;
using Foundry.Deploy.Services.Networking;

namespace Foundry.Deploy.Services.Runtime;

public sealed class DeploymentRuntimeContextService : IDeploymentRuntimeContextService
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";
    private const string CacheVolumeLabel = "Foundry Cache";
    private const string RuntimeFolderName = "Runtime";
    private readonly IVolumeDiscovery _volumeDiscovery;
    private readonly Func<string, string?> _environmentVariableReader;
    private readonly DeploymentNetworkPolicy _networkPolicy;

    public DeploymentRuntimeContextService()
        : this(new WindowsVolumeDiscovery())
    {
    }

    public DeploymentRuntimeContextService(IVolumeDiscovery volumeDiscovery, DeploymentNetworkPolicy? networkPolicy = null)
        : this(volumeDiscovery, Environment.GetEnvironmentVariable, networkPolicy)
    {
    }

    internal DeploymentRuntimeContextService(
        IVolumeDiscovery volumeDiscovery,
        Func<string, string?> environmentVariableReader, DeploymentNetworkPolicy? networkPolicy = null)
    {
        _volumeDiscovery = volumeDiscovery;
        _environmentVariableReader = environmentVariableReader;
        _networkPolicy = networkPolicy ?? new(false);
    }

    public DeploymentRuntimeContext Resolve()
    {
        if (_networkPolicy.OfflineOnly)
        {
            string? associated = _environmentVariableReader("FOUNDRY_VERIFIED_CACHE_ROOT");
            return !string.IsNullOrWhiteSpace(associated) && Path.IsPathFullyQualified(associated)
                ? new(DeploymentMode.Usb, Path.Combine(associated, RuntimeFolderName))
                : new(DeploymentMode.Iso, null);
        }
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
