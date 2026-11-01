// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Carries the interactive Autopilot options that Foundry.Deploy hands to the interactive registration runtime.
/// </summary>
public sealed record DeployAutopilotInteractiveSettings
{
    /// <summary>
    /// Gets whether the technician types a custom group tag directly for devices not yet registered in Autopilot.
    /// </summary>
    public bool PreselectCustomGroupTag { get; init; }

    /// <summary>
    /// Gets whether the technician can enter the user whose UPN is prefilled at OOBE.
    /// </summary>
    public bool OfferAssignedUser { get; init; }
}
