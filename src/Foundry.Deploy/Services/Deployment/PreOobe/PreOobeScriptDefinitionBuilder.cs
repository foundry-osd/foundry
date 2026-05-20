using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.DriverPacks;
using System.Text.Json;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Builds the ordered set of pre-OOBE scripts required by deployment customizations.
/// </summary>
public sealed class PreOobeScriptDefinitionBuilder
{
    private const string RemoveAppxPackageCatalogFileName = "Remove-AppX.packages.json";
    private const string RemoveAiComponentsSettingsFileName = "Remove-AiComponents.settings.json";

    /// <summary>
    /// Builds script definitions for selected AppX removal and optional deferred driver provisioning.
    /// </summary>
    public IReadOnlyList<PreOobeScriptDefinition> Build(
        DeployAppxRemovalSettings appxRemoval,
        PreOobeDriverPackScriptSettings? driverPack = null)
    {
        return Build(appxRemoval, new DeployAiComponentRemovalSettings(), driverPack);
    }

    /// <summary>
    /// Builds script definitions for selected customizations and optional deferred driver provisioning.
    /// </summary>
    public IReadOnlyList<PreOobeScriptDefinition> Build(
        DeployAppxRemovalSettings appxRemoval,
        DeployAiComponentRemovalSettings aiComponentRemoval,
        PreOobeDriverPackScriptSettings? driverPack = null)
    {
        ArgumentNullException.ThrowIfNull(appxRemoval);
        ArgumentNullException.ThrowIfNull(aiComponentRemoval);

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
                DataFiles =
                [
                    new PreOobeScriptDataFile
                    {
                        FileName = RemoveAppxPackageCatalogFileName,
                        Content = BuildRemoveAppxPackageCatalog(packageNames)
                    }
                ]
            });
        }

        if (aiComponentRemoval.IsEnabled && HasAnyAiComponentRemovalAppxOptionEnabled(aiComponentRemoval))
        {
            scripts.Add(new PreOobeScriptDefinition
            {
                Id = "remove-ai-components",
                FileName = "Remove-AiComponents.ps1",
                ResourceName = PreOobeScriptResources.RemoveAiComponents,
                Priority = PreOobeScriptPriority.Customization,
                DataFiles =
                [
                    new PreOobeScriptDataFile
                    {
                        FileName = RemoveAiComponentsSettingsFileName,
                        Content = BuildRemoveAiComponentsSettings(aiComponentRemoval)
                    }
                ]
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

    private static string BuildRemoveAppxPackageCatalog(IReadOnlyList<string> packageNames)
    {
        string json = JsonSerializer.Serialize(
            packageNames.Select(packageName => new
            {
                packageName
            }),
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        return json + Environment.NewLine;
    }

    private static string BuildRemoveAiComponentsSettings(DeployAiComponentRemovalSettings settings)
    {
        string json = JsonSerializer.Serialize(
            new
            {
                appxPackages = BuildRemoveAiComponentsAppxPackages(settings),
            },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

        return json + Environment.NewLine;
    }

    private static object[] BuildRemoveAiComponentsAppxPackages(DeployAiComponentRemovalSettings settings)
    {
        var packages = new List<object>();
        if (settings.RemoveCopilot)
        {
            packages.Add(new { packageName = "Microsoft.Copilot" });
        }

        if (settings.RemoveAiHub)
        {
            packages.Add(new { packageName = "Microsoft.Windows.AIHub" });
        }

        return packages.ToArray();
    }

    private static bool HasAnyAiComponentRemovalAppxOptionEnabled(DeployAiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.RemoveAiHub;
    }
}
