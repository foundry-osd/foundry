// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

/// <summary>
/// Identifies the firmware mode used to start the current Windows PE session.
/// </summary>
public enum FirmwareMode
{
    Unknown,
    Bios,
    Uefi
}
