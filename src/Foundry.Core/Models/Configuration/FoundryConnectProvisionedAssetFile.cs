namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes one file copied into the Foundry.Connect provisioning workspace.
/// </summary>
public sealed record FoundryConnectProvisionedAssetFile
{
    /// <summary>
    /// Gets the absolute or staging-root source path of the generated asset.
    /// </summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the destination path relative to the Connect provisioning root.
    /// </summary>
    public string RelativeDestinationPath { get; init; } = string.Empty;
}
