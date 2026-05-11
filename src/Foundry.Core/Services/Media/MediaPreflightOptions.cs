using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Media;

public sealed record MediaPreflightOptions
{
    public bool IsAdkReady { get; init; }
    public bool IsNetworkConfigurationReady { get; init; }
    public bool IsDeployConfigurationReady { get; init; }
    public bool IsConnectProvisioningReady { get; init; }
    public bool AreRequiredSecretsReady { get; init; }
    public bool IsAutopilotEnabled { get; init; }
    public bool IsAutopilotConfigurationReady { get; init; } = true;
    public string? AutopilotProfileDisplayName { get; init; }
    public string? AutopilotProfileFolderName { get; init; }
    public bool IsFinalExecutionEnabled { get; init; }
    public string IsoOutputPath { get; init; } = string.Empty;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;
    public UsbPartitionStyle UsbPartitionStyle { get; init; } = UsbPartitionStyle.Gpt;
    public UsbFormatMode UsbFormatMode { get; init; } = UsbFormatMode.Quick;
    public string WinPeLanguage { get; init; } = string.Empty;
    public IReadOnlyList<string> AvailableWinPeLanguages { get; init; } = [];
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;
    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = [];
    public string? CustomDriverDirectoryPath { get; init; }
    public WinPeUsbDiskCandidate? SelectedUsbDisk { get; init; }
}
