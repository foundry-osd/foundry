namespace Foundry.Deploy.Models;

public sealed record HardwareProfile
{
    public string Manufacturer { get; init; } = "Unknown";
    public string Model { get; init; } = "Unknown";
    public string Product { get; init; } = "Unknown";
    public string SerialNumber { get; init; } = "Unknown";
    public string Architecture { get; init; } = string.Empty;
    public bool IsVirtualMachine { get; init; }
    public bool IsOnBattery { get; init; }
    public bool IsTpmPresent { get; init; }
    public bool IsAutopilotCapable { get; init; }
    public string SystemFirmwareHardwareId { get; init; } = string.Empty;
    public IReadOnlyList<PnpDeviceInfo> PnpDevices { get; init; } = Array.Empty<PnpDeviceInfo>();

    public string DisplayLabel => $"{Manufacturer} | {Model} | {Product} | {Architecture}";
}
