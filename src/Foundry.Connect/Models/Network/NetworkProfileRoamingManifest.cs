namespace Foundry.Connect.Models.Network;

/// <summary>
/// Describes Foundry-managed network profile material captured in WinPE for Windows import.
/// </summary>
public sealed record NetworkProfileRoamingManifest
{
    /// <summary>
    /// Gets the manifest schema version.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets the last manifest update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the captured Wi-Fi profile metadata.
    /// </summary>
    public NetworkProfileRoamingProfile? WifiProfile { get; init; }

    /// <summary>
    /// Gets the captured wired 802.1X profile metadata.
    /// </summary>
    public NetworkProfileRoamingProfile? WiredDot1xProfile { get; init; }

    /// <summary>
    /// Gets captured certificate metadata.
    /// </summary>
    public IReadOnlyList<NetworkProfileRoamingCertificate> Certificates { get; init; } = [];
}

/// <summary>
/// Describes one captured network profile file.
/// </summary>
public sealed record NetworkProfileRoamingProfile
{
    /// <summary>
    /// Gets the profile relative path inside the artifact root.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Foundry source that produced this profile.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Gets the expected pre-OOBE connectivity behavior.
    /// </summary>
    public string ConnectivityExpectation { get; init; } = string.Empty;
}

/// <summary>
/// Describes one captured certificate file.
/// </summary>
public sealed record NetworkProfileRoamingCertificate
{
    /// <summary>
    /// Gets the certificate relative path inside the artifact root.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the certificate kind.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets the target Windows certificate store name.
    /// </summary>
    public string StoreName { get; init; } = string.Empty;
}
