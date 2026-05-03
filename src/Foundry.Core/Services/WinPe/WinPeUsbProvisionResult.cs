namespace Foundry.Core.Services.WinPe;

public sealed record WinPeUsbProvisionResult
{
    public string BootDriveLetter { get; init; } = string.Empty;
    public string CacheDriveLetter { get; init; } = string.Empty;
}
