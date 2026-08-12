// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores the Windows optional feature changes selected by the administrator.
/// </summary>
public sealed record WindowsOptionalFeatureSettings
{
    public bool IsEnabled { get; init; }

    public IReadOnlyList<string> EnabledFeatureIds { get; init; } = [];

    public IReadOnlyList<string> DisabledFeatureIds { get; init; } = [];
}
