// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Carries WinPE bootstrap settings in the generated deployment configuration.
/// </summary>
public sealed record DeployLocalizationSettings
{
    /// <summary>
    /// Gets the optional Windows time-zone identifier read by the WinPE bootstrap.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }
}
