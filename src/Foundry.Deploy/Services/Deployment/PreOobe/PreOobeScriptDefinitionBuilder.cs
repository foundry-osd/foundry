using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Builds the ordered set of pre-OOBE scripts required by deployment customizations.
/// </summary>
public sealed class PreOobeScriptDefinitionBuilder
{
    /// <summary>
    /// Builds script definitions for selected AppX removal and optional deferred driver provisioning.
    /// </summary>
    public IReadOnlyList<PreOobeScriptDefinition> Build(
        DeployAppxRemovalSettings appxRemoval,
        PreOobeDriverPackScriptSettings? driverPack = null)
    {
        ArgumentNullException.ThrowIfNull(appxRemoval);

        var scripts = new List<PreOobeScriptDefinition>();

        if (driverPack is not null && driverPack.CommandKind != DeferredDriverPackageCommandKind.None)
        {
            scripts.Add(new PreOobeScriptDefinition
            {
                Id = "driver-pack",
                FileName = "Install-DriverPack.ps1",
                ResourceName = PreOobeScriptResources.InstallDriverPack,
                Priority = PreOobeScriptPriority.DriverProvisioning,
                Arguments =
                [
                    "-CommandKind",
                    driverPack.CommandKind.ToString(),
                    "-PackagePath",
                    driverPack.RuntimePackagePath
                ]
            });
        }

        string[] packageNames = appxRemoval.IsEnabled
            ? appxRemoval.PackageNames
                .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
                .Select(packageName => packageName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        if (packageNames.Length > 0)
        {
            scripts.Add(new PreOobeScriptDefinition
            {
                Id = "remove-appx",
                FileName = "Remove-AppX.ps1",
                ResourceName = PreOobeScriptResources.RemoveAppx,
                Priority = PreOobeScriptPriority.Customization,
                Arguments = ["-PackageNames", .. packageNames]
            });
        }

        if (driverPack is not null && driverPack.CommandKind != DeferredDriverPackageCommandKind.None)
        {
            scripts.Add(new PreOobeScriptDefinition
            {
                Id = "cleanup",
                FileName = "Cleanup-PreOobe.ps1",
                ResourceName = PreOobeScriptResources.CleanupPreOobe,
                Priority = PreOobeScriptPriority.Cleanup
            });
        }

        return scripts;
    }
}
