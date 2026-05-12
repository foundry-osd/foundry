namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Defines the HTTP probes used to determine Internet reachability.
/// </summary>
public sealed class InternetProbeOptions
{
    /// <summary>
    /// Gets the ordered probe URIs attempted by network status refreshes.
    /// </summary>
    public IReadOnlyList<string> ProbeUris { get; init; } =
    [
        "http://www.msftconnecttest.com/connecttest.txt",
        "http://www.google.com"
    ];

    /// <summary>
    /// Gets the timeout applied to each probe request.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 5;
}
