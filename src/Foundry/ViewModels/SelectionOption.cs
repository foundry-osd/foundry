// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.ViewModels;

/// <summary>
/// Represents a typed value paired with display text for combo boxes and option selectors.
/// </summary>
/// <typeparam name="T">Type of the backing value.</typeparam>
/// <param name="Value">Backing value used by the workflow.</param>
/// <param name="DisplayName">User-facing option label.</param>
public sealed record SelectionOption<T>(T Value, string DisplayName);
