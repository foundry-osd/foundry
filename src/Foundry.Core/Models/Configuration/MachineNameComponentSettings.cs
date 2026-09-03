// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes one ordered computer-name component and its applicable formatting options.
/// </summary>
public sealed record MachineNameComponentSettings
{
    public MachineNameComponentType Type { get; init; }

    public string? StaticText { get; init; }

    public int? MaximumLength { get; init; }

    public MachineNameTruncation? Truncation { get; init; }
}
