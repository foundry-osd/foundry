// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores the user-authored deployment media protection preference.
/// </summary>
public sealed record DeploymentProtectionSettings
{
    /// <summary>
    /// Gets a value indicating whether Foundry Deploy requires a media password.
    /// </summary>
    public bool IsEnabled { get; init; }
}
