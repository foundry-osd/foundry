// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

public sealed record DeployMachineNameComponentSettings
{
    public MachineNameComponentType Type { get; init; }

    public string? StaticText { get; init; }

    public int? MaximumLength { get; init; }

    public MachineNameTruncation? Truncation { get; init; }
}
