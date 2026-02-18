namespace Foundry.Services.WinPe;

public sealed record WinPeUsbDiskCandidate
{
    public int DiskNumber { get; init; }
    public string FriendlyName { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string UniqueId { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public bool? IsRemovable { get; init; }
    public bool IsSystem { get; init; }
    public bool IsBoot { get; init; }
    public ulong SizeBytes { get; init; }

    public string DisplayLabel
    {
        get
        {
            double sizeGb = SizeBytes / 1024d / 1024d / 1024d;
            return $"Disk {DiskNumber} - {FriendlyName} ({sizeGb:F1} GB)";
        }
    }
}
