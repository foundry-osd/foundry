// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes one optional feature state change consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployWindowsOptionalFeatureAction
{
    public string Id { get; init; } = string.Empty;

    public bool Enable { get; init; }
}
