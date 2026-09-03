// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Contains machine-naming validation issues and the configured maximum length.
/// </summary>
/// <param name="Issues">Validation issues.</param>
/// <param name="MaximumLength">Maximum possible composed-name length.</param>
public sealed record MachineNamingValidationResult(
    IReadOnlyList<MachineNamingValidationIssue> Issues,
    int MaximumLength)
{
    public bool IsValid => Issues.Count == 0;
}
