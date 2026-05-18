namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes one provisioned AppX package exposed by the Foundry customization catalog.
/// </summary>
public sealed record AppxRemovalCatalogEntry
{
    /// <summary>
    /// Gets the provisioned AppX package identifier matched against the provisioned package display name.
    /// </summary>
    public required string PackageName { get; init; }

    /// <summary>
    /// Gets the label shown in Foundry OSD.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the category used for grouping and profile selection.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets whether the package is part of the recommended default cleanup selection.
    /// </summary>
    public bool DefaultSelected { get; init; }
}
