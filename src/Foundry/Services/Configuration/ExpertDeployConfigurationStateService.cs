using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Serilog;

namespace Foundry.Services.Configuration;

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

    public event EventHandler? StateChanged;

    public FoundryExpertConfigurationDocument Current { get; private set; }

    public bool IsNetworkConfigurationReady => EvaluateNetworkMediaReadiness().IsNetworkConfigurationReady;

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

    public bool IsConnectProvisioningReady => EvaluateNetworkMediaReadiness().IsConnectProvisioningReady;

    public bool AreRequiredSecretsReady => EvaluateNetworkMediaReadiness().AreRequiredSecretsReady;

    public void UpdateLocalization(LocalizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Current with { Localization = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateNetwork(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        networkSecretStateService.Update(settings);
        Current = Current with { Network = NetworkConfigurationValidator.SanitizeForPersistence(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GenerateDeployConfigurationJson()
    {
        return deployConfigurationGenerator.Serialize(deployConfigurationGenerator.Generate(Current));
    }

    public FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath)
    {
        FoundryExpertConfigurationDocument document = Current with
        {
            Network = networkSecretStateService.ApplyRequiredSecrets(Current.Network)
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
            Network = NetworkConfigurationValidator.SanitizeForPersistence(document.Network)
        };
    }

    private NetworkMediaReadinessEvaluation EvaluateNetworkMediaReadiness()
    {
        return NetworkMediaReadinessEvaluator.Evaluate(Current.Network, networkSecretStateService.PersonalWifiPassphrase);
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
