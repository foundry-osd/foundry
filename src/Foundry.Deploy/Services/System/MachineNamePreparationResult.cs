// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Deploy.Services.System;

public sealed record MachineNamePreparationResult
{
    public string? ComputerName { get; init; }

    public MachineNameCompositionFailureKind FailureKind { get; init; }

    public MachineNameComponentType? ComponentType { get; init; }

    public bool IsSuccess => FailureKind == MachineNameCompositionFailureKind.None && ComputerName is not null;
}
