namespace Foundry.Core.Services.WinPe;

public sealed record WinPeUsbDiskCandidate
{
    public int DiskNumber { get; init; }
    public string FriendlyName { get; init; } = string.Empty;
    public string DriveLetters { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string UniqueId { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public bool? IsRemovable { get; init; }
    public bool IsSystem { get; init; }
    public bool IsBoot { get; init; }
    public ulong SizeBytes { get; init; }
}
