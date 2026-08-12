// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models.Configuration;

public sealed record DeployWindowsOptionalFeatureAction
{
    public string Id { get; init; } = string.Empty;
    public bool Enable { get; init; }
}
