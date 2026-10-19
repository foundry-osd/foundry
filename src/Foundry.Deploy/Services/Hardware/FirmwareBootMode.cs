// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Hardware;

/// <summary>
/// Identifies the firmware interface that started the running operating system session.
/// </summary>
public enum FirmwareBootMode
{
    /// <summary>The boot mode could not be determined.</summary>
    Unknown,

    /// <summary>The device started in legacy BIOS or compatibility support module (CSM) mode.</summary>
    Bios,

    /// <summary>The device started in UEFI mode.</summary>
    Uefi
}
