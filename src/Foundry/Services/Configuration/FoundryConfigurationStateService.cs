// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Autopilot;
using Foundry.Telemetry;
using Serilog;
using AppSettingsService = Foundry.Services.Settings.IAppSettingsService;

namespace Foundry.Services.Configuration;

/// <summary>
/// Maintains the user-facing Foundry configuration state and generates deploy/connect payloads from it.
/// </summary>
/// <remarks>
/// Secrets that should not be persisted are kept in <see cref="INetworkSecretStateService"/> and are only merged
/// when a provisioning bundle is generated.
/// </remarks>
internal sealed class FoundryConfigurationStateService : IFoundryConfigurationStateService
{
    private readonly IFoundryConfigurationService foundryConfigurationService;
    private readonly IDeployConfigurationGenerator deployConfigurationGenerator;
    private readonly IConnectConfigurationGenerator connectConfigurationGenerator;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly IAutopilotHardwareHashSessionState autopilotHardwareHashSessionState;
    private readonly AppSettingsService appSettingsService;
    private readonly ILogger logger;

    public FoundryConfigurationStateService(
        IFoundryConfigurationService foundryConfigurationService,
        IDeployConfigurationGenerator deployConfigurationGenerator,
        IConnectConfigurationGenerator connectConfigurationGenerator,
        INetworkSecretStateService networkSecretStateService,
        IAutopilotHardwareHashSessionState autopilotHardwareHashSessionState,
        AppSettingsService appSettingsService,
        ILogger logger)
    {
        this.foundryConfigurationService = foundryConfigurationService;
        this.deployConfigurationGenerator = deployConfigurationGenerator;
        this.connectConfigurationGenerator = connectConfigurationGenerator;
        this.networkSecretStateService = networkSecretStateService;
        this.autopilotHardwareHashSessionState = autopilotHardwareHashSessionState;
        this.appSettingsService = appSettingsService;
        this.logger = logger.ForContext<FoundryConfigurationStateService>();
        Current = SanitizeForPersistence(Load());
        Save();
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public FoundryConfigurationDocument Current { get; private set; }

    /// <inheritdoc />
    public bool IsNetworkConfigurationReady => EvaluateNetworkMediaReadiness().IsNetworkConfigurationReady;

    /// <inheritdoc />
    public bool IsDeployConfigurationReady
    {
        get
        {
            try
            {
                byte[]? mediaSecretsKey = Current.Autopilot.IsEnabled &&
                                          Current.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload
                    ? MediaSecretEnvelopeProtector.GenerateMediaKey()
                    : null;
                _ = GenerateDeployConfigurationJson(mediaSecretsKey: mediaSecretsKey);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                logger.Warning(ex, "Foundry configuration is not ready.");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool IsConnectProvisioningReady => EvaluateNetworkMediaReadiness().IsConnectProvisioningReady;

    /// <inheritdoc />
    public bool AreRequiredSecretsReady => EvaluateNetworkMediaReadiness().AreRequiredSecretsReady;

    /// <inheritdoc />
    public bool IsAutopilotEnabled => Current.Autopilot.IsEnabled;

    /// <inheritdoc />
    public bool IsAutopilotConfigurationReady => AutopilotConfigurationValidation.IsReady;

    /// <inheritdoc />
    public AutopilotConfigurationValidationResult AutopilotConfigurationValidation =>
        AutopilotConfigurationValidator.Evaluate(CreateAutopilotSettingsForValidation(Current.Autopilot), DateTimeOffset.UtcNow);

    /// <inheritdoc />
    public AutopilotProvisioningMode AutopilotProvisioningMode => Current.Autopilot.ProvisioningMode;

    /// <inheritdoc />
    public string? SelectedAutopilotProfileDisplayName => Current.Autopilot.IsEnabled &&
                                                          Current.Autopilot.ProvisioningMode == AutopilotProvisioningMode.JsonProfile
        ? GetSelectedAutopilotProfile()?.DisplayName
        : null;

    /// <inheritdoc />
    public string? SelectedAutopilotProfileFolderName => Current.Autopilot.IsEnabled &&
                                                         Current.Autopilot.ProvisioningMode == AutopilotProvisioningMode.JsonProfile
        ? GetSelectedAutopilotProfile()?.FolderName
        : null;

    /// <inheritdoc />
    public void UpdateGeneral(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { General = SanitizeGeneralForPersistence(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateLocalization(LocalizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { Localization = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateOperatingSystemSelection(OperatingSystemSelectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { OperatingSystemSelection = OperatingSystemSelectionSettingsNormalizer.Normalize(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateNetwork(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        networkSecretStateService.Update(settings);
        Current = Current with { Network = NetworkConfigurationValidator.SanitizeForPersistence(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateCustomization(CustomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { Customization = SanitizeCustomizationForPersistence(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateAutopilot(AutopilotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { Autopilot = SanitizeAutopilotForPersistence(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateTelemetry(TelemetrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { Telemetry = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public string GenerateDeployConfigurationJson(TelemetrySettings? telemetryOverride = null, byte[]? mediaSecretsKey = null)
    {
        FoundryConfigurationDocument document = CreateDocumentForDeployGeneration(telemetryOverride);

        return deployConfigurationGenerator.Serialize(deployConfigurationGenerator.Generate(document, mediaSecretsKey));
    }

    /// <inheritdoc />
    public FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath, TelemetrySettings? telemetryOverride = null)
    {
        FoundryConfigurationDocument document = Current with
        {
            Network = networkSecretStateService.ApplyRequiredSecrets(Current.Network),
            Telemetry = telemetryOverride ?? Current.Telemetry
        };

        return connectConfigurationGenerator.CreateProvisioningBundle(document, stagingDirectoryPath);
    }

    private FoundryConfigurationDocument Load()
    {
        if (!File.Exists(Constants.FoundryConfigurationStatePath))
        {
            return FoundryConfigurationMigration.ApplyLegacyGeneralSettings(
                CreateDefaultDocument(),
                appSettingsService.MigratedGeneralSettings);
        }

        try
        {
            string json = File.ReadAllText(Constants.FoundryConfigurationStatePath);
            return foundryConfigurationService.Deserialize(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            string backupPath = Constants.FoundryConfigurationStatePath + ".invalid";
            TryMoveInvalidState(backupPath, ex);
            return CreateDefaultDocument();
        }
    }

    private FoundryConfigurationDocument CreateDocumentForDeployGeneration(TelemetrySettings? telemetryOverride)
    {
        return Current with
        {
            Autopilot = CreateAutopilotSettingsForValidation(Current.Autopilot),
            Telemetry = telemetryOverride ?? Current.Telemetry
        };
    }

    private AutopilotSettings CreateAutopilotSettingsForValidation(AutopilotSettings settings)
    {
        AutopilotHardwareHashUploadSettings hardwareHashUpload = settings.HardwareHashUpload with
        {
            BootMediaCertificate = autopilotHardwareHashSessionState.BootMediaCertificate
        };
        if (settings.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload &&
            !autopilotHardwareHashSessionState.HasConnectedTenant)
        {
            hardwareHashUpload = hardwareHashUpload with
            {
                KnownGroupTags = [],
                DefaultGroupTag = null
            };
        }

        return settings with
        {
            HardwareHashUpload = hardwareHashUpload
        };
    }

    private static FoundryConfigurationDocument CreateDefaultDocument()
    {
        return new FoundryConfigurationDocument
        {
            General = new GeneralSettings
            {
                IsoOutputPath = Path.Combine(Constants.IsoWorkspaceDirectoryPath, "Foundry.iso")
            }
        };
    }

    private void Save()
    {
        Directory.CreateDirectory(Constants.ConfigurationWorkspaceDirectoryPath);
        FoundryConfigurationDocument document = SanitizeForPersistence(Current);
        string json = foundryConfigurationService.Serialize(document);
        File.WriteAllText(Constants.FoundryConfigurationStatePath, json);
    }

    private static FoundryConfigurationDocument SanitizeForPersistence(FoundryConfigurationDocument document)
    {
        return document with
        {
            General = SanitizeGeneralForPersistence(document.General),
            Network = NetworkConfigurationValidator.SanitizeForPersistence(document.Network),
            OperatingSystemSelection = OperatingSystemSelectionSettingsNormalizer.Normalize(document.OperatingSystemSelection),
            Customization = SanitizeCustomizationForPersistence(document.Customization),
            Autopilot = SanitizeAutopilotForPersistence(document.Autopilot)
        };
    }

    private static GeneralSettings SanitizeGeneralForPersistence(GeneralSettings settings)
    {
        return settings.Architecture == WinPeArchitecture.Arm64 && settings.UsbPartitionStyle == UsbPartitionStyle.Mbr
            ? settings with { UsbPartitionStyle = UsbPartitionStyle.Gpt }
            : settings;
    }

    private static AutopilotSettings SanitizeAutopilotForPersistence(AutopilotSettings settings)
    {
        // Keep persisted profiles deterministic because the selected profile is referenced by ID across sessions.
        AutopilotProfileSettings[] profiles = settings.Profiles
            .Where(profile =>
                !string.IsNullOrWhiteSpace(profile.Id) &&
                !string.IsNullOrWhiteSpace(profile.DisplayName) &&
                !string.IsNullOrWhiteSpace(profile.FolderName) &&
                !string.IsNullOrWhiteSpace(profile.Source) &&
                !string.IsNullOrWhiteSpace(profile.JsonContent))
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? defaultProfileId = profiles.Any(profile =>
            string.Equals(profile.Id, settings.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            ? settings.DefaultProfileId
            : profiles.FirstOrDefault()?.Id;

        return settings with
        {
            DefaultProfileId = defaultProfileId,
            Profiles = profiles,
            HardwareHashUpload = SanitizeHardwareHashUploadSettings(settings.HardwareHashUpload)
        };
    }

    private static AutopilotHardwareHashUploadSettings SanitizeHardwareHashUploadSettings(
        AutopilotHardwareHashUploadSettings? settings)
    {
        if (settings?.Tenant is null)
        {
            return new AutopilotHardwareHashUploadSettings();
        }

        string[] knownGroupTags = (settings.KnownGroupTags ?? [])
            .Select(groupTag => groupTag.Trim())
            .Where(groupTag => !string.IsNullOrWhiteSpace(groupTag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(groupTag => groupTag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? defaultGroupTag = NormalizeOptional(settings.DefaultGroupTag);
        if (!string.IsNullOrWhiteSpace(defaultGroupTag) &&
            !knownGroupTags.Contains(defaultGroupTag, StringComparer.OrdinalIgnoreCase))
        {
            defaultGroupTag = null;
        }

        return settings with
        {
            Tenant = new AutopilotTenantRegistrationSettings
            {
                TenantId = NormalizeOptional(settings.Tenant.TenantId),
                ApplicationObjectId = NormalizeOptional(settings.Tenant.ApplicationObjectId),
                ClientId = NormalizeOptional(settings.Tenant.ClientId),
                ServicePrincipalObjectId = NormalizeOptional(settings.Tenant.ServicePrincipalObjectId)
            },
            ActiveCertificate = settings.ActiveCertificate is null
                ? null
                : settings.ActiveCertificate with
                {
                    KeyId = NormalizeOptional(settings.ActiveCertificate.KeyId),
                    Thumbprint = NormalizeOptional(settings.ActiveCertificate.Thumbprint)?.ToUpperInvariant(),
                    DisplayName = NormalizeOptional(settings.ActiveCertificate.DisplayName)
                },
            KnownGroupTags = knownGroupTags,
            DefaultGroupTag = defaultGroupTag
        };
    }

    private static CustomizationSettings SanitizeCustomizationForPersistence(CustomizationSettings settings)
    {
        string normalizedPrefix = ComputerNameTemplate.NormalizePrefix(settings.MachineNaming.Prefix?.Trim());
        string? prefix = string.IsNullOrWhiteSpace(normalizedPrefix)
            ? null
            : normalizedPrefix;

        return settings with
        {
            // Disabled machine naming must not leak a stale prefix into generated deployment JSON.
            MachineNaming = new MachineNamingSettings
            {
                IsEnabled = settings.MachineNaming.IsEnabled,
                Prefix = settings.MachineNaming.IsEnabled ? prefix : null,
                AutoGenerateName = settings.MachineNaming.IsEnabled && settings.MachineNaming.AutoGenerateName,
                AllowManualSuffixEdit = !settings.MachineNaming.IsEnabled || settings.MachineNaming.AllowManualSuffixEdit
            },
            Oobe = SanitizeOobeForPersistence(settings.Oobe),
            AppxRemoval = SanitizeAppxRemovalForPersistence(settings.AppxRemoval),
            AiComponentRemoval = SanitizeAiComponentRemovalForPersistence(
                settings.AiComponentRemoval,
                settings.AppxRemoval)
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static OobeSettings SanitizeOobeForPersistence(OobeSettings settings)
    {
        return settings.IsEnabled
            ? settings
            : new OobeSettings();
    }

    private static AppxRemovalSettings SanitizeAppxRemovalForPersistence(AppxRemovalSettings settings)
    {
        string[] packageNames = NormalizeAppxRemovalPackageNames(settings.PackageNames);
        return settings.IsEnabled
            ? new AppxRemovalSettings
            {
                IsEnabled = true,
                PackageNames = packageNames
            }
            : new AppxRemovalSettings();
    }

    private static AiComponentRemovalSettings SanitizeAiComponentRemovalForPersistence(
        AiComponentRemovalSettings settings,
        AppxRemovalSettings legacyAppxRemoval)
    {
        bool removeCopilot = settings.IsEnabled && settings.RemoveCopilot ||
            HasLegacyAppxRemovalPackage(legacyAppxRemoval, "Microsoft.Copilot");
        bool removeAiHub = settings.IsEnabled && settings.RemoveAiHub ||
            HasLegacyAppxRemovalPackage(legacyAppxRemoval, "Microsoft.Windows.AIHub");
        var migratedSettings = new AiComponentRemovalSettings
        {
            IsEnabled = settings.IsEnabled || removeCopilot || removeAiHub,
            RemoveCopilot = removeCopilot,
            RemoveAiHub = removeAiHub,
            DisableRecall = settings.IsEnabled && settings.DisableRecall,
            DisableClickToDo = settings.IsEnabled && settings.DisableClickToDo,
            DisableAiServiceAutoStart = settings.IsEnabled && settings.DisableAiServiceAutoStart,
            DisableEdgeAi = settings.IsEnabled && settings.DisableEdgeAi,
            DisablePaintAi = settings.IsEnabled && settings.DisablePaintAi,
            DisableNotepadAi = settings.IsEnabled && settings.DisableNotepadAi
        };

        return migratedSettings.IsEnabled && HasAnyAiComponentRemovalOptionEnabled(migratedSettings)
            ? migratedSettings
            : new AiComponentRemovalSettings();
    }

    private static bool HasAnyAiComponentRemovalOptionEnabled(AiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.RemoveAiHub ||
            settings.DisableRecall ||
            settings.DisableClickToDo ||
            settings.DisableAiServiceAutoStart ||
            settings.DisableEdgeAi ||
            settings.DisablePaintAi ||
            settings.DisableNotepadAi;
    }

    private static bool HasLegacyAppxRemovalPackage(AppxRemovalSettings settings, string packageName)
    {
        return settings.IsEnabled &&
            settings.PackageNames.Any(value => string.Equals(value.Trim(), packageName, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeAppxRemovalPackageNames(IEnumerable<string> packageNames)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string packageName in packageNames)
        {
            string trimmed = packageName.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                !AppxRemovalCatalog.ContainsPackageName(trimmed) ||
                !seen.Add(trimmed))
            {
                continue;
            }

            result.Add(trimmed);
        }

        return result.ToArray();
    }

    private NetworkMediaReadinessEvaluation EvaluateNetworkMediaReadiness()
    {
        return NetworkMediaReadinessEvaluator.Evaluate(Current.Network, networkSecretStateService.PersonalWifiPassphrase);
    }

    private AutopilotProfileSettings? GetSelectedAutopilotProfile()
    {
        if (string.IsNullOrWhiteSpace(Current.Autopilot.DefaultProfileId))
        {
            return null;
        }

        return Current.Autopilot.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, Current.Autopilot.DefaultProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private void TryMoveInvalidState(string backupPath, Exception originalException)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(Constants.FoundryConfigurationStatePath, backupPath);
            logger.Warning(
                originalException,
                "Foundry configuration state was invalid and defaults were restored. StatePath={StatePath}, BackupPath={BackupPath}",
                Constants.FoundryConfigurationStatePath,
                backupPath);
        }
        catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
        {
            logger.Error(
                backupException,
                "Failed to back up invalid Foundry configuration state. StatePath={StatePath}, BackupPath={BackupPath}, OriginalError={OriginalError}",
                Constants.FoundryConfigurationStatePath,
                backupPath,
                originalException.Message);
            throw;
        }
    }
}
