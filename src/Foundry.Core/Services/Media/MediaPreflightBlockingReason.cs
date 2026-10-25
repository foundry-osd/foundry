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
    WinPeKeyboardLayoutUnavailable,
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

    /// <summary>
    /// An included custom image index reports a known architecture that differs from the selected media architecture.
    /// </summary>
    CustomImageArchitectureMismatch,

    FinalExecutionDeferred,

    /// <summary>
    /// The selected drive is too small even for a USB drive without cache, which only needs room for the media.
    /// </summary>
    UsbTargetBelowMinimumSizeWithoutCache,

    /// <summary>A follower is still downloading or installing the shared content of the applied publication.</summary>
    SharedContentSyncing,

    /// <summary>Some shared content of the applied publication could not be imported.</summary>
    SharedContentFailed,

    /// <summary>Shared content of the applied publication stayed unavailable for many checks.</summary>
    SharedContentStalled
}
