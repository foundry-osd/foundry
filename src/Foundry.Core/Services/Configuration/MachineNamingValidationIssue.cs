// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Describes one machine-naming validation issue.
/// </summary>
/// <param name="Code">Validation failure code.</param>
/// <param name="ComponentIndex">Affected component index, when applicable.</param>
public sealed record MachineNamingValidationIssue(MachineNamingValidationCode Code, int? ComponentIndex = null);
