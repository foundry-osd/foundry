using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Hardware;

internal sealed record HardwareProfileSnapshot(
    string Manufacturer,
    string Model,
    string Product,
    string SerialNumber,
    bool IsOnBattery,
    IReadOnlyList<PnpDeviceInfo> Devices);
