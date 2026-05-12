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
    /// Gets the optional computer-name prefix.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets whether deployment should generate a suffix automatically.
    /// </summary>
    public bool AutoGenerateName { get; init; }

    /// <summary>
    /// Gets whether users may edit the generated suffix before deployment.
    /// </summary>
    public bool AllowManualSuffixEdit { get; init; } = true;
}
