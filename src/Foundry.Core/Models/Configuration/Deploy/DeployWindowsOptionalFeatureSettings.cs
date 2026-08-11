// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes optional feature state changes consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployWindowsOptionalFeatureSettings
{
    public bool IsEnabled { get; init; }

    public IReadOnlyList<DeployWindowsOptionalFeatureAction> Actions { get; init; } = [];
}
