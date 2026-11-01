// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.Device;

/// <summary>
/// Hardware identity of the device being registered, shown in the application footer.
/// </summary>
/// <param name="Manufacturer">System manufacturer, or <see langword="null"/> when it could not be read.</param>
/// <param name="Model">System model, or <see langword="null"/> when it could not be read.</param>
/// <param name="SerialNumber">BIOS serial number, or <see langword="null"/> when it could not be read.</param>
public sealed record DeviceInformation(string? Manufacturer, string? Model, string? SerialNumber);
