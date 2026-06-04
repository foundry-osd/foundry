namespace Foundry.Deploy.Services.Network;

internal sealed record NetworkProfileRoamingManifest
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public NetworkProfileRoamingProfile? WifiProfile { get; init; }
    public NetworkProfileRoamingProfile? WiredDot1xProfile { get; init; }
    public IReadOnlyList<NetworkProfileRoamingCertificate> Certificates { get; init; } = [];
}

internal sealed record NetworkProfileRoamingProfile
{
    public string RelativePath { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string ConnectivityExpectation { get; init; } = string.Empty;
}

internal sealed record NetworkProfileRoamingCertificate
{
    public string RelativePath { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string StoreName { get; init; } = string.Empty;
    public string? PasswordSecretRelativePath { get; init; }
}
