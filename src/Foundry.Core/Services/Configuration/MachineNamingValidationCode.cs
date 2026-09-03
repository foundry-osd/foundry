// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Identifies a machine-naming configuration error.
/// </summary>
public enum MachineNamingValidationCode
{
    None,
    UnsupportedMode,
    UnsupportedSeparator,
    UnsupportedCasing,
    InvalidManualInitialValue,
    ComponentsRequired,
    UnsupportedComponentType,
    DuplicateComponentType,
    StaticTextRequired,
    StaticTextBecomesEmpty,
    MaximumLengthRequired,
    MaximumLengthOutOfRange,
    TruncationRequired,
    UnsupportedTruncation,
    UnexpectedStaticText,
    UnexpectedMaximumLength,
    UnexpectedTruncation,
    CharacterBudgetExceeded
}
