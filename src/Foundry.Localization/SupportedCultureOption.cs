// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Localization;

/// <summary>
/// Represents a supported UI culture option for language selection controls.
/// </summary>
/// <param name="Code">Canonical culture code.</param>
/// <param name="DisplayName">Localized display name.</param>
/// <param name="IsSelected">Whether the option matches the active culture.</param>
public sealed record SupportedCultureOption(string Code, string DisplayName, bool IsSelected);
