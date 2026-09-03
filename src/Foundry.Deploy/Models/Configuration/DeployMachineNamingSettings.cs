// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration;
using DeployMachineNameComponentSettings = Foundry.Core.Models.Configuration.Deploy.DeployMachineNameComponentSettings;

namespace Foundry.Deploy.Models.Configuration;

public sealed record DeployMachineNamingSettings
{
    public bool IsEnabled { get; init; }
    public MachineNamingMode Mode { get; init; } = MachineNamingMode.Manual;
    public string? ManualInitialValue { get; init; }
    public IReadOnlyList<DeployMachineNameComponentSettings> Components { get; init; } = [];
    public MachineNameSeparator Separator { get; init; }
    public MachineNameCasing Casing { get; init; }
    public bool AllowEditingDuringDeployment { get; init; } = true;

    [JsonPropertyName("prefix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPrefix { get; init; }

    [JsonPropertyName("autoGenerateName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyAutoGenerateName { get; init; }

    [JsonPropertyName("allowManualSuffixEdit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyAllowManualSuffixEdit { get; init; }
}
