// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Autopilot;

/// <summary>
/// A Windows Autopilot device identity registered in the tenant, as visible through Microsoft Graph.
/// </summary>
/// <param name="Id">The Autopilot device identity ID.</param>
/// <param name="GroupTag">The group tag currently assigned to the device, if any.</param>
/// <param name="DisplayName">The device display name currently assigned in Autopilot, if any.</param>
/// <param name="UserPrincipalName">The user currently assigned to the device, if any.</param>
public sealed record AutopilotRegisteredDevice(
    string Id,
    string? GroupTag,
    string? DisplayName,
    string? UserPrincipalName);

/// <summary>
/// Outcome of a serial number lookup in Windows Autopilot devices.
/// </summary>
public enum AutopilotDeviceLookupStatus
{
    /// <summary>No registered device matches the serial number.</summary>
    NotFound,

    /// <summary>Exactly one registered device matches the serial number.</summary>
    Found,

    /// <summary>More than one registered device matches, so no single device can be trusted.</summary>
    Ambiguous
}

/// <summary>
/// Result of a serial number lookup. <see cref="Device"/> is set only when <see cref="Status"/> is
/// <see cref="AutopilotDeviceLookupStatus.Found"/>.
/// </summary>
public sealed record AutopilotDeviceLookup(
    AutopilotDeviceLookupStatus Status,
    AutopilotRegisteredDevice? Device);
