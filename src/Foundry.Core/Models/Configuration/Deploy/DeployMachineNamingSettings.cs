// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

public sealed record DeployMachineNamingSettings
{
    public bool IsEnabled { get; init; }

    public MachineNamingMode Mode { get; init; } = MachineNamingMode.Manual;

    public string? ManualInitialValue { get; init; }

    public IReadOnlyList<DeployMachineNameComponentSettings> Components { get; init; } = [];

    public MachineNameSeparator Separator { get; init; }

    public MachineNameCasing Casing { get; init; }

    public bool AllowEditingDuringDeployment { get; init; } = true;
}
