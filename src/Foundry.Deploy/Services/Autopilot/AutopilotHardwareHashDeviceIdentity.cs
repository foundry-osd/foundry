// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Contains the device identity values captured by OA3Tool and used by Autopilot import.
/// </summary>
/// <param name="SerialNumber">Device serial number reported by the OA3 hardware report.</param>
/// <param name="HardwareHash">Autopilot hardware hash reported by OA3Tool.</param>
/// <param name="GroupTag">Optional Autopilot group tag selected by the operator.</param>
public sealed record AutopilotHardwareHashDeviceIdentity(
    string SerialNumber,
    string HardwareHash,
    string? GroupTag);
