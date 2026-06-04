namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Describes whether Foundry.Deploy should import eligible network profile material before OOBE.
/// </summary>
public sealed record DeployNetworkProfileRoamingSettings
{
    /// <summary>
    /// Gets the default WinPE artifact root where Foundry.Connect writes captured network profile material.
    /// </summary>
    public const string DefaultArtifactRootPath = @"X:\Foundry\Runtime\NetworkProfileRoaming";

    /// <summary>
    /// Gets whether eligible Foundry-managed network profile roaming is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets whether explicitly configured PFX/private-key material may be imported.
    /// </summary>
    public bool IncludePrivateKeyMaterial { get; init; }

    /// <summary>
    /// Gets the artifact root path consumed by Foundry.Deploy inside WinPE.
    /// </summary>
    public string ArtifactRootPath { get; init; } = DefaultArtifactRootPath;
}
