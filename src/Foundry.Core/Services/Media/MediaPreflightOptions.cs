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
    /// Gets a value indicating whether Autopilot staging is enabled.
    /// </summary>
    public bool IsAutopilotEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether the selected Autopilot profile is valid.
    /// </summary>
    public bool IsAutopilotConfigurationReady { get; init; } = true;

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
    /// Gets an optional custom driver directory.
    /// </summary>
    public string? CustomDriverDirectoryPath { get; init; }

    /// <summary>
    /// Gets the selected USB target disk, if USB creation is requested.
    /// </summary>
    public WinPeUsbDiskCandidate? SelectedUsbDisk { get; init; }
}
