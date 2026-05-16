using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;
using Serilog;

namespace Foundry.Services.Configuration;

/// <summary>
/// Maintains the user-facing Expert Deploy configuration state and generates deploy/connect payloads from it.
/// </summary>
/// <remarks>
/// Secrets that should not be persisted are kept in <see cref="INetworkSecretStateService"/> and are only merged
/// when a provisioning bundle is generated.
/// </remarks>
internal sealed class ExpertDeployConfigurationStateService : IExpertDeployConfigurationStateService
{
    private readonly IExpertConfigurationService expertConfigurationService;
    private readonly IDeployConfigurationGenerator deployConfigurationGenerator;
    private readonly IConnectConfigurationGenerator connectConfigurationGenerator;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly ILogger logger;

    public ExpertDeployConfigurationStateService(
        IExpertConfigurationService expertConfigurationService,
        IDeployConfigurationGenerator deployConfigurationGenerator,
        IConnectConfigurationGenerator connectConfigurationGenerator,
        INetworkSecretStateService networkSecretStateService,
        ILogger logger)
    {
        this.expertConfigurationService = expertConfigurationService;
        this.deployConfigurationGenerator = deployConfigurationGenerator;
        this.connectConfigurationGenerator = connectConfigurationGenerator;
        this.networkSecretStateService = networkSecretStateService;
        this.logger = logger.ForContext<ExpertDeployConfigurationStateService>();
        Current = SanitizeForPersistence(Load());
        Save();
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public FoundryExpertConfigurationDocument Current { get; private set; }

    /// <inheritdoc />
    public bool IsNetworkConfigurationReady => EvaluateNetworkMediaReadiness().IsNetworkConfigurationReady;

    /// <inheritdoc />
    public bool IsDeployConfigurationReady
    {
        get
        {
            try
            {
                _ = GenerateDeployConfigurationJson();
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                logger.Warning(ex, "Expert Deploy configuration is not ready.");
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
    public bool IsAutopilotConfigurationReady => !Current.Autopilot.IsEnabled || GetSelectedAutopilotProfile() is not null;

    /// <inheritdoc />
    public string? SelectedAutopilotProfileDisplayName => Current.Autopilot.IsEnabled
        ? GetSelectedAutopilotProfile()?.DisplayName
        : null;

    /// <inheritdoc />
    public string? SelectedAutopilotProfileFolderName => Current.Autopilot.IsEnabled
        ? GetSelectedAutopilotProfile()?.FolderName
        : null;

    /// <inheritdoc />
    public void UpdateLocalization(LocalizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { Localization = settings };
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
    public string GenerateDeployConfigurationJson(TelemetrySettings? telemetryOverride = null)
    {
        FoundryExpertConfigurationDocument document = telemetryOverride is null
            ? Current
            : Current with { Telemetry = telemetryOverride };

        return deployConfigurationGenerator.Serialize(deployConfigurationGenerator.Generate(document));
    }

    /// <inheritdoc />
    public FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath, TelemetrySettings? telemetryOverride = null)
    {
        FoundryExpertConfigurationDocument document = Current with
        {
            Network = networkSecretStateService.ApplyRequiredSecrets(Current.Network),
            Telemetry = telemetryOverride ?? Current.Telemetry
        };

        return connectConfigurationGenerator.CreateProvisioningBundle(document, stagingDirectoryPath);
    }

    private FoundryExpertConfigurationDocument Load()
    {
        if (!File.Exists(Constants.ExpertDeployConfigurationStatePath))
        {
            return new FoundryExpertConfigurationDocument();
        }

        try
        {
            string json = File.ReadAllText(Constants.ExpertDeployConfigurationStatePath);
            return expertConfigurationService.Deserialize(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            string backupPath = Constants.ExpertDeployConfigurationStatePath + ".invalid";
            TryMoveInvalidState(backupPath, ex);
            return new FoundryExpertConfigurationDocument();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Constants.ConfigurationWorkspaceDirectoryPath);
        FoundryExpertConfigurationDocument document = SanitizeForPersistence(Current);
        string json = expertConfigurationService.Serialize(document);
        File.WriteAllText(Constants.ExpertDeployConfigurationStatePath, json);
    }

    private static FoundryExpertConfigurationDocument SanitizeForPersistence(FoundryExpertConfigurationDocument document)
    {
        return document with
        {
            Network = NetworkConfigurationValidator.SanitizeForPersistence(document.Network),
            Customization = SanitizeCustomizationForPersistence(document.Customization),
            Autopilot = SanitizeAutopilotForPersistence(document.Autopilot)
        };
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
            Profiles = profiles
        };
    }

    private static CustomizationSettings SanitizeCustomizationForPersistence(CustomizationSettings settings)
    {
        string normalizedPrefix = ComputerNameRules.Normalize(settings.MachineNaming.Prefix?.Trim());
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
            Oobe = SanitizeOobeForPersistence(settings.Oobe)
        };
    }

    private static OobeSettings SanitizeOobeForPersistence(OobeSettings settings)
    {
        return settings.IsEnabled
            ? settings
            : new OobeSettings();
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

            File.Move(Constants.ExpertDeployConfigurationStatePath, backupPath);
            logger.Warning(
                originalException,
                "Expert Deploy configuration state was invalid and defaults were restored. StatePath={StatePath}, BackupPath={BackupPath}",
                Constants.ExpertDeployConfigurationStatePath,
                backupPath);
        }
        catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
        {
            logger.Error(
                backupException,
                "Failed to back up invalid Expert Deploy configuration state. StatePath={StatePath}, BackupPath={BackupPath}, OriginalError={OriginalError}",
                Constants.ExpertDeployConfigurationStatePath,
                backupPath,
                originalException.Message);
            throw;
        }
    }
}
