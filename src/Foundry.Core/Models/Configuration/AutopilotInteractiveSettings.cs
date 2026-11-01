// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the technician-facing options of the interactive Autopilot registration mode.
/// </summary>
public sealed record AutopilotInteractiveSettings
{
    /// <summary>
    /// Gets whether the technician types a custom group tag directly for devices not yet registered in Autopilot,
    /// instead of choosing one from the known group tags.
    /// </summary>
    public bool PreselectCustomGroupTag { get; init; }

    /// <summary>
    /// Gets whether the technician can enter the user whose UPN is prefilled at OOBE.
    /// </summary>
    public bool OfferAssignedUser { get; init; }
}
