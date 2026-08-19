// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Utilities.IO;

namespace Foundry.Services.Configuration;

internal sealed class ConfigurationOverviewService : IConfigurationOverviewService
{
    private readonly IAdkService adkService;
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly IWinPeLanguageDiscoveryService languageDiscoveryService;

    public ConfigurationOverviewService(
        IAdkService adkService,
        IFoundryConfigurationStateService configurationStateService,
        IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService,
        INetworkSecretStateService networkSecretStateService,
        IWinPeLanguageDiscoveryService languageDiscoveryService)
    {
        this.adkService = adkService;
        this.configurationStateService = configurationStateService;
        this.deploymentProtectionSecretStateService = deploymentProtectionSecretStateService;
        this.networkSecretStateService = networkSecretStateService;
        this.languageDiscoveryService = languageDiscoveryService;
    }

    public ConfigurationOverviewEvaluation Evaluate()
    {
        FoundryConfigurationDocument configuration = configurationStateService.Current;
        return ConfigurationOverviewEvaluator.Evaluate(new ConfigurationOverviewContext
        {
            Configuration = configuration,
            EffectiveNetwork = networkSecretStateService.ApplyRequiredSecrets(configuration.Network),
            IsWinPeLanguageReady = IsWinPeLanguageReady(configuration.General),
            IsCustomDriverConfigurationReady = IsCustomDriverConfigurationReady(configuration.General),
            IsDeploymentProtectionSecretReady = !configuration.General.DeploymentProtection.IsEnabled ||
                deploymentProtectionSecretStateService.IsValid,
            IsAutopilotConfigurationReady = configurationStateService.IsAutopilotConfigurationReady
        });
    }

    private bool IsWinPeLanguageReady(GeneralSettings settings)
    {
        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(settings.WinPeLanguage))
        {
            return false;
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            return true;
        }

        WinPeResult<IReadOnlyList<string>> languagesResult = languageDiscoveryService.GetAvailableLanguages(
            new WinPeLanguageDiscoveryOptions
            {
                Tools = toolsResult.Value,
                Architecture = settings.Architecture
            });
        return !languagesResult.IsSuccess || languagesResult.Value is null || languagesResult.Value.Count == 0 ||
            languagesResult.Value.Contains(settings.WinPeLanguage, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCustomDriverConfigurationReady(GeneralSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.CustomDriverDirectoryPath) ||
            (Directory.Exists(settings.CustomDriverDirectoryPath) &&
             FileSearch.ContainsRecursive(settings.CustomDriverDirectoryPath, "*.inf"));
    }
}
