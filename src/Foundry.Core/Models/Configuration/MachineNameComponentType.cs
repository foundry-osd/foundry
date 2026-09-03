// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Identifies a value that can participate in a composed computer name.
/// </summary>
public enum MachineNameComponentType
{
    StaticText,
    SerialNumber,
    Manufacturer,
    Model,
    AssetTag,
    SystemUuid,
    Random
}
