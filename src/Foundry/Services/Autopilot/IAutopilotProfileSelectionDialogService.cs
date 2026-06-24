// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Shows the Autopilot profile selection dialog used by Foundry configuration.
/// </summary>
public interface IAutopilotProfileSelectionDialogService
{
    /// <summary>
    /// Lets the user select one or more downloaded Autopilot profiles.
    /// </summary>
    /// <param name="availableProfiles">Profiles available for selection.</param>
    /// <returns>Selected profiles, or <see langword="null"/> when the dialog is canceled.</returns>
    Task<IReadOnlyList<AutopilotProfileSettings>?> PickProfilesAsync(
        IReadOnlyList<AutopilotProfileSettings> availableProfiles);
}
