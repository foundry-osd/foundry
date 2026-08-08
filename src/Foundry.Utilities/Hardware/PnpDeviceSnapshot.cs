// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Hardware;

/// <summary>
/// Describes raw Plug and Play device facts.
/// </summary>
public sealed record PnpDeviceSnapshot(
    string Name,
    string DeviceId,
    IReadOnlyList<string> HardwareIds,
    string ClassGuid,
    string Manufacturer,
    string PnpClass);
