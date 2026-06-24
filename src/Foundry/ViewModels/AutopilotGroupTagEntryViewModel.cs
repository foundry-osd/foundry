// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.ViewModels;

/// <summary>
/// Represents one tenant-discovered Autopilot group tag for selection and display.
/// </summary>
public sealed record AutopilotGroupTagEntryViewModel(string DisplayName, string? GroupTag);
