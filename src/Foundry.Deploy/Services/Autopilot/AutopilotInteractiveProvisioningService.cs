// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using Foundry.Core.Models.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Installs <c>Foundry.AutopilotInteractive</c> into the retained Foundry root of the applied Windows image and writes its configuration.
/// </summary>
public sealed class AutopilotInteractiveProvisioningService : IAutopilotInteractiveProvisioningService
{
    /// <summary>Installed executable, relative to the retained Foundry root that Foundry.PostInstall resolves.</summary>
    internal const string RuntimeExecutableRelativePath =
        AutopilotInteractiveLayout.RuntimeRelativePath + "/" + AutopilotInteractiveLayout.ExecutableFileName;

    /// <summary>Runtime configuration, relative to the retained Foundry root that Foundry.PostInstall resolves.</summary>
    internal const string RuntimeConfigRelativePath =
        AutopilotInteractiveLayout.RuntimeRelativePath + "/" + AutopilotInteractiveRuntimeConfiguration.FileName;

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly AutopilotInteractiveRuntimeSource _source;

    /// <summary>Creates the service with the production runtime source.</summary>
    public AutopilotInteractiveProvisioningService()
        : this(new AutopilotInteractiveRuntimeSource())
    {
    }

    internal AutopilotInteractiveProvisioningService(AutopilotInteractiveRuntimeSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public async Task<AutopilotInteractiveProvisioningResult> ProvisionAsync(
        string targetWindowsPartitionRoot,
        AutopilotInteractiveRuntimeOrigin origin,
        AutopilotInteractiveRuntimeConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWindowsPartitionRoot);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(configuration);

        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(targetWindowsPartitionRoot);
        string runtimeDirectory = layout.RuntimeAutopilotInteractive;
        Directory.CreateDirectory(runtimeDirectory);
        Directory.CreateDirectory(layout.StateAutopilotInteractive);
        Directory.CreateDirectory(layout.LogsAutopilotInteractive);

        await _source.InstallAsync(origin, runtimeDirectory, progress, cancellationToken).ConfigureAwait(false);

        string configPath = Path.Combine(runtimeDirectory, AutopilotInteractiveRuntimeConfiguration.FileName);
        DeploymentFilePublication.WriteAllText(configPath,
            AutopilotInteractiveRuntimeConfigurationSerializer.Serialize(configuration) + Environment.NewLine, Utf8NoBom);
        return new AutopilotInteractiveProvisioningResult(
            runtimeDirectory,
            Path.Combine(runtimeDirectory, AutopilotInteractiveLayout.ExecutableFileName),
            configPath);
    }
}
