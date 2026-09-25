// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

public sealed record DeployMachineNamingSettings
{
    public bool IsEnabled { get; init; }

    /// <summary>Gets whether the final deployment name is assigned to the Autopilot device during hardware hash upload.</summary>
    public bool UploadComputerNameToAutopilot { get; init; }

    public MachineNamingMode Mode { get; init; } = MachineNamingMode.Manual;

    public string? ManualInitialValue { get; init; }

    public IReadOnlyList<DeployMachineNameComponentSettings> Components { get; init; } = [];

    public MachineNameSeparator Separator { get; init; }

    public MachineNameCasing Casing { get; init; }

    public bool AllowEditingDuringDeployment { get; init; } = true;
}
