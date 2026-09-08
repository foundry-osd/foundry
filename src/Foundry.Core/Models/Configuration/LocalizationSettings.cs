// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored Windows PE localization preferences.
/// </summary>
public sealed record LocalizationSettings
{
    /// <summary>
    /// Gets the optional Windows time-zone identifier used by the WinPE bootstrap.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }
}
