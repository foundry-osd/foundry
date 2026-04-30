using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Models.Configuration;

public sealed record GeneralSettings
{
    public string? IsoOutputPath { get; init; }
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public string? WinPeLanguage { get; init; }
    public bool UseCa2023 { get; init; }
    public UsbPartitionStyle UsbPartitionStyle { get; init; } = UsbPartitionStyle.Gpt;
    public UsbFormatMode UsbFormatMode { get; init; } = UsbFormatMode.Quick;
    public bool IncludeDellDrivers { get; init; }
    public bool IncludeHpDrivers { get; init; }
    public string? CustomDriverDirectoryPath { get; init; }
}
