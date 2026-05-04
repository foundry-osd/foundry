namespace Foundry.Core.Services.Media;

public enum MediaPreflightBlockingReason
{
    AdkNotReady,
    InvalidIsoPath,
    MissingWinPeLanguage,
    WinPeLanguageUnavailable,
    RuntimePayloadNotReady,
    NetworkConfigurationNotReady,
    DeployConfigurationNotReady,
    ConnectProvisioningNotReady,
    RequiredSecretsNotReady,
    NoUsbTarget,
    Arm64RequiresGpt,
    CustomDriverDirectoryNotFound,
    CustomDriverDirectoryHasNoInfFiles,
    FinalExecutionDeferred
}
