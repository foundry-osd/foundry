// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
    UsbTargetBelowMinimumSize,
    Arm64RequiresGpt,
    CustomDriverDirectoryNotFound,
    CustomDriverDirectoryHasNoInfFiles,
    FinalExecutionDeferred
}
