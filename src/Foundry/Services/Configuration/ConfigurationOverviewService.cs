// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Utilities.Globalization;
using Foundry.Utilities.IO;

namespace Foundry.Services.Configuration;

internal sealed class ConfigurationOverviewService : IConfigurationOverviewService
{
    private readonly object syncRoot = new();
    private readonly IAdkService adkService;
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly IOobeAccountSecretStateService oobeAccountSecretStateService;
    private readonly IDomainJoinSecretStateService domainJoinSecretStateService;
    private readonly IWinPeLanguageDiscoveryService languageDiscoveryService;
    private ConfigurationOverviewEvaluation? cachedEvaluation;

    public ConfigurationOverviewService(
        IAdkService adkService,
        IFoundryConfigurationStateService configurationStateService,
        IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService,
        INetworkSecretStateService networkSecretStateService,
        IOobeAccountSecretStateService oobeAccountSecretStateService,
        IDomainJoinSecretStateService domainJoinSecretStateService,
        IWinPeLanguageDiscoveryService languageDiscoveryService)
    {
        this.adkService = adkService;
        this.configurationStateService = configurationStateService;
        this.deploymentProtectionSecretStateService = deploymentProtectionSecretStateService;
        this.networkSecretStateService = networkSecretStateService;
        this.oobeAccountSecretStateService = oobeAccountSecretStateService;
        this.domainJoinSecretStateService = domainJoinSecretStateService;
        this.languageDiscoveryService = languageDiscoveryService;

        adkService.StatusChanged += OnAdkStatusChanged;
        configurationStateService.StateChanged += OnUnderlyingStateChanged;
        deploymentProtectionSecretStateService.Changed += OnUnderlyingStateChanged;
        networkSecretStateService.Changed += OnUnderlyingStateChanged;
        oobeAccountSecretStateService.Changed += OnUnderlyingStateChanged;
        domainJoinSecretStateService.Changed += OnUnderlyingStateChanged;
    }

    public event EventHandler? Changed;

    public ConfigurationOverviewEvaluation Evaluate()
    {
        lock (syncRoot)
        {
            if (cachedEvaluation is not null)
            {
                return cachedEvaluation;
            }

            FoundryConfigurationDocument configuration = configurationStateService.Current;
            cachedEvaluation = ConfigurationOverviewEvaluator.Evaluate(new ConfigurationOverviewContext
            {
                Configuration = configuration,
                NetworkInputs = configurationStateService.MediaInputs,
                IsWinPeLanguageReady = IsWinPeLanguageReady(configuration.General),
                IsCustomDriverConfigurationReady = IsCustomDriverConfigurationReady(configuration.General),
                IsDeploymentProtectionSecretReady = !configuration.General.DeploymentProtection.IsEnabled ||
                    deploymentProtectionSecretStateService.IsValid,
                IsOobeAccountConfigurationReady = oobeAccountSecretStateService.Validate(configuration.Customization.Oobe).IsValid,
                IsAutopilotConfigurationReady = configurationStateService.IsAutopilotConfigurationReady,
                IsDomainJoinConfigurationReady = configurationStateService.IsDomainJoinConfigurationReady,
                IsCustomImagesReady = configurationStateService.IsCustomImagesReady,
                IsPostInstallationReady = configurationStateService.IsPostInstallationReady,
                IsCustomDriversReady = configurationStateService.IsCustomDriversReady,
                IsUnattendConfigurationReady = configurationStateService.IsUnattendConfigurationReady
            });
            return cachedEvaluation;
        }
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e) => Invalidate();

    private void OnUnderlyingStateChanged(object? sender, EventArgs e) => Invalidate();

    private void Invalidate()
    {
        lock (syncRoot)
        {
            cachedEvaluation = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
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
        bool isLanguageAvailable = !languagesResult.IsSuccess || languagesResult.Value is null || languagesResult.Value.Count == 0 ||
            languagesResult.Value.Contains(settings.WinPeLanguage, StringComparer.OrdinalIgnoreCase);
        return isLanguageAvailable && IsWinPeKeyboardLayoutInstalled(settings.WinPeKeyboardLayoutId);
    }

    // A layout chosen on another computer may be missing here; media creation blocks on it, so the overview flags it too.
    private static bool IsWinPeKeyboardLayoutInstalled(string? layoutId)
    {
        return !KeyboardLayoutSelection.IsUnavailable(KeyboardLayoutCatalog.GetInstalled(), layoutId);
    }

    private static bool IsCustomDriverConfigurationReady(GeneralSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.CustomDriverDirectoryPath) ||
            (Directory.Exists(settings.CustomDriverDirectoryPath) &&
             FileSearch.ContainsRecursive(settings.CustomDriverDirectoryPath, "*.inf"));
    }
}
