// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Hardware;

/// <summary>
/// Reads the firmware boot mode of the running session so deployment can reject layouts the device cannot start.
/// </summary>
public interface IFirmwareBootModeReader
{
    /// <summary>
    /// Gets the firmware boot mode without throwing; unreadable sources return <see cref="FirmwareBootMode.Unknown"/>.
    /// </summary>
    /// <returns>The detected firmware boot mode.</returns>
    FirmwareBootMode GetBootMode();
}
