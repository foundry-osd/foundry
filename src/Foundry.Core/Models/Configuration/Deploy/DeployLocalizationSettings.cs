// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Carries deploy-runtime localization settings that are not OS catalog selectors.
/// </summary>
public sealed record DeployLocalizationSettings
{
    /// <summary>
    /// Gets the optional default Windows time-zone identifier.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }
}
