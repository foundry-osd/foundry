// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Supplies structured settings and target values for machine-name composition.
/// </summary>
public sealed record MachineNameCompositionRequest
{
    public required IReadOnlyList<MachineNameComponentSettings> Components { get; init; }

    public required IReadOnlyDictionary<MachineNameComponentType, string?> Values { get; init; }

    public required string RandomValue { get; init; }

    public MachineNameSeparator Separator { get; init; }

    public MachineNameCasing Casing { get; init; }
}
