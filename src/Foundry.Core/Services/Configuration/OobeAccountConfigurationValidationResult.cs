// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public sealed record OobeAccountConfigurationValidationResult(
    IReadOnlyList<OobeAccountConfigurationValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
