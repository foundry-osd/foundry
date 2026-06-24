// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml.Media;

namespace Foundry.ViewModels;

/// <summary>
/// Represents one tenant readiness value displayed in the Autopilot hardware hash upload configuration.
/// </summary>
public sealed record AutopilotTenantReadinessEntryViewModel(string Name, string Value, Brush? ValueForeground);
