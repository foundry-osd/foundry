namespace Foundry.Deploy.Models;

public sealed record TargetDiskInfo
{
    public int DiskNumber { get; init; }
    public string FriendlyName { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public string PartitionStyle { get; init; } = string.Empty;
    public ulong SizeBytes { get; init; }
    public bool IsSystem { get; init; }
    public bool IsBoot { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsOffline { get; init; }
    public bool IsRemovable { get; init; }
    public bool IsSelectable { get; init; }
    public string SelectionWarning { get; init; } = string.Empty;

    public string DisplayLabel
    {
        get
        {
            string sizeGiB = SizeBytes > 0
                ? $"{(SizeBytes / 1024d / 1024d / 1024d):0.0} GiB"
                : "Unknown size";

            string warningSuffix = string.IsNullOrWhiteSpace(SelectionWarning)
                ? string.Empty
                : $" | {SelectionWarning}";

            return $"Disk {DiskNumber} | {FriendlyName} | {sizeGiB} | {BusType}{warningSuffix}";
        }
    }
}
