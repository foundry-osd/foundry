namespace Foundry.Core.Services.Media;

public enum MediaPreflightBlockingReason
{
    AdkNotReady,
    InvalidIsoPath,
    MissingWinPeLanguage,
    WinPeLanguageUnavailable,
    NetworkConfigurationNotReady,
    DeployConfigurationNotReady,
    ConnectProvisioningNotReady,
    RequiredSecretsNotReady,
    AutopilotConfigurationNotReady,
    NoUsbTarget,
    Arm64RequiresGpt,
    CustomDriverDirectoryNotFound,
    CustomDriverDirectoryHasNoInfFiles,
    FinalExecutionDeferred
}
