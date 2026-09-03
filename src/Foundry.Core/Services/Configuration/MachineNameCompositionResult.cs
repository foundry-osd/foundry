// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public enum MachineNameCompositionFailureKind
{
    None,
    InvalidConfiguration,
    MissingHardwareValue,
    PlaceholderHardwareValue,
    EmptyAfterSanitization,
    InvalidRandomValue,
    InvalidFinalName
}

/// <summary>
/// Contains a composed name or a precise failure that prevented composition.
/// </summary>
public sealed record MachineNameCompositionResult
{
    public string? ComputerName { get; init; }

    public MachineNameCompositionFailureKind FailureKind { get; init; }

    public MachineNameComponentType? ComponentType { get; init; }

    public MachineNamingValidationResult? ConfigurationValidation { get; init; }

    public bool IsSuccess => FailureKind == MachineNameCompositionFailureKind.None && ComputerName is not null;
}
