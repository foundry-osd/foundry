// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Media;

/// <summary>
/// Captures current shell readiness and media settings before evaluating available media actions.
/// </summary>
public sealed record MediaPreflightOptions
{
    /// <summary>
    /// Gets a value indicating whether required ADK tools are available.
    /// </summary>
    public bool IsAdkReady { get; init; }

    /// <summary>
    /// Gets a value indicating whether network configuration is valid enough for media generation.
    /// </summary>
    public bool IsNetworkConfigurationReady { get; init; }

    /// <summary>
    /// Gets a value indicating whether deployment configuration can be generated.
    /// </summary>
    public bool IsDeployConfigurationReady { get; init; }

    /// <summary>
    /// Gets a value indicating whether Connect provisioning files can be generated.
    /// </summary>
    public bool IsConnectProvisioningReady { get; init; }

    /// <summary>
    /// Gets a value indicating whether required volatile secrets are available.
    /// </summary>
    public bool AreRequiredSecretsReady { get; init; }

    /// <summary>
    /// Gets a value indicating whether Autopilot provisioning is enabled.
    /// </summary>
    public bool IsAutopilotEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether the selected Autopilot provisioning mode is valid.
    /// </summary>
    public bool IsAutopilotConfigurationReady { get; init; } = true;

    /// <summary>
    /// Gets the detailed Autopilot validation code for the selected provisioning mode.
    /// </summary>
    public AutopilotConfigurationValidationCode AutopilotConfigurationValidationCode { get; init; } =
        AutopilotConfigurationValidationCode.Ready;

    /// <summary>
    /// Gets the selected Autopilot provisioning mode.
    /// </summary>
    public AutopilotProvisioningMode AutopilotProvisioningMode { get; init; } = AutopilotProvisioningMode.JsonProfile;

    /// <summary>
    /// Gets the selected Autopilot profile display name when available.
    /// </summary>
    public string? AutopilotProfileDisplayName { get; init; }

    /// <summary>
    /// Gets the selected Autopilot profile folder name when available.
    /// </summary>
    public string? AutopilotProfileFolderName { get; init; }

    /// <summary>
    /// Gets a value indicating whether final media execution is enabled by the shell.
    /// </summary>
    public bool IsFinalExecutionEnabled { get; init; }

    /// <summary>
    /// Gets the ISO output path currently configured.
    /// </summary>
    public string IsoOutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the selected WinPE architecture.
    /// </summary>
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;

    /// <summary>
    /// Gets the selected boot signature mode.
    /// </summary>
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;

    /// <summary>
    /// Gets the selected USB partition style.
    /// </summary>
    public UsbPartitionStyle UsbPartitionStyle { get; init; } = UsbPartitionStyle.Gpt;

    /// <summary>
    /// Gets the selected USB formatting mode.
    /// </summary>
    public UsbFormatMode UsbFormatMode { get; init; } = UsbFormatMode.Quick;

    /// <summary>
    /// Gets the selected WinPE language code.
    /// </summary>
    public string WinPeLanguage { get; init; } = string.Empty;

    /// <summary>
    /// Gets the language codes currently available for WinPE customization.
    /// </summary>
    public IReadOnlyList<string> AvailableWinPeLanguages { get; init; } = [];

    /// <summary>
    /// Gets the selected boot image source.
    /// </summary>
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;

    /// <summary>
    /// Gets selected vendor driver packs for WinPE media.
    /// </summary>
    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = [];

    /// <summary>
    /// Gets optional folders that contain drivers to inject into the boot image.
    /// </summary>
    public IReadOnlyList<string> CustomDriverDirectoryPaths { get; init; } = [];

    /// <summary>
    /// Gets the selected USB target disk, if USB creation is requested.
    /// </summary>
    public WinPeUsbDiskCandidate? SelectedUsbDisk { get; init; }

    /// <summary>
    /// Gets the WinPE optional component names selected for the boot image. When empty, Foundry's
    /// recommended defaults are applied.
    /// </summary>
    public IReadOnlyList<string> OptionalComponents { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the boot image firewall is enabled.
    /// </summary>
    public bool EnableFirewall { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the minimized troubleshooting PowerShell console is included.
    /// </summary>
    public bool IncludeTroubleshootingConsole { get; init; }

    /// <summary>
    /// Gets a value indicating whether a copy of the boot.wim is kept next to the ISO after creation.
    /// </summary>
    public bool KeepBootWimCopy { get; init; }

    /// <summary>
    /// Gets a value indicating whether PowerShell 7 is integrated into the boot image.
    /// </summary>
    public bool IncludePowerShell7 { get; init; }

    /// <summary>
    /// Gets the selected PowerShell 7 release version. When empty, the latest stable release is resolved.
    /// </summary>
    public string? PowerShell7Version { get; init; }

    /// <summary>
    /// Gets the PowerShell modules selected for integration into the boot image.
    /// </summary>
    public IReadOnlyList<PowerShellModuleSelection> PowerShellModules { get; init; } = [];

    /// <summary>
    /// Gets the additional folders whose contents are copied into the boot image root.
    /// </summary>
    public IReadOnlyList<string> AdditionalRootFolderPaths { get; init; } = [];
}
