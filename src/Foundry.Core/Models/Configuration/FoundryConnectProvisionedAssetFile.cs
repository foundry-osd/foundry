namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes one file copied into the Foundry.Connect provisioning workspace.
/// </summary>
public sealed record FoundryConnectProvisionedAssetFile
{
    /// <summary>
    /// Gets the existing staged file path to copy into the mounted image.
    /// </summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the destination path relative to the mounted image root.
    /// </summary>
    public string RelativeDestinationPath { get; init; } = string.Empty;
}
