// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

public sealed record PnpDeviceInfo
{
    public string Name { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public IReadOnlyList<string> HardwareIds { get; init; } = Array.Empty<string>();
    public string ClassGuid { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string PnpClass { get; init; } = string.Empty;
}
