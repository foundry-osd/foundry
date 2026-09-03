// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes computer-name customization rules generated for deployment.
/// </summary>
public sealed record MachineNamingSettings
{
    /// <summary>
    /// Gets whether computer-name customization is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets how the computer name is supplied.
    /// </summary>
    public MachineNamingMode Mode { get; init; } = MachineNamingMode.Manual;

    /// <summary>
    /// Gets the optional complete value used to prefill manual naming.
    /// </summary>
    public string? ManualInitialValue { get; init; }

    /// <summary>
    /// Gets the ordered components used by composed naming.
    /// </summary>
    public IReadOnlyList<MachineNameComponentSettings> Components { get; init; } = [];

    /// <summary>
    /// Gets the separator inserted between resolved components.
    /// </summary>
    public MachineNameSeparator Separator { get; init; }

    /// <summary>
    /// Gets the casing applied to the complete resolved name.
    /// </summary>
    public MachineNameCasing Casing { get; init; }

    /// <summary>
    /// Gets whether the technician may edit a composed name during deployment.
    /// </summary>
    public bool AllowEditingDuringDeployment { get; init; } = true;

    [JsonIgnore]
    public string? Prefix { get; init; }

    [JsonIgnore]
    public bool AutoGenerateName { get; init; }

    [JsonIgnore]
    public bool AllowManualSuffixEdit { get; init; } = true;

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
