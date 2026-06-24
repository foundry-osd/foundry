// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models.Configuration;

public sealed record DeployMachineNamingSettings
{
    public bool IsEnabled { get; init; }
    public string? Prefix { get; init; }
    public bool AutoGenerateName { get; init; }
    public bool AllowManualSuffixEdit { get; init; } = true;
}
