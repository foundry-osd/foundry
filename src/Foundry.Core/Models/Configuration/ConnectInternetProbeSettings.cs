namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines the HTTP probes Foundry.Connect uses to validate Internet reachability.
/// </summary>
public sealed record ConnectInternetProbeSettings
{
    /// <summary>
    /// Gets the ordered probe URIs attempted by the runtime network checks.
    /// </summary>
    public IReadOnlyList<string> ProbeUris { get; init; } =
    [
        "http://www.msftconnecttest.com/connecttest.txt",
        "http://www.google.com"
    ];

    /// <summary>
    /// Gets the per-probe timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 5;
}
