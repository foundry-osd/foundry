namespace Foundry.Core.Models.Configuration;

public sealed record ConnectInternetProbeSettings
{
    public IReadOnlyList<string> ProbeUris { get; init; } =
    [
        "http://www.msftconnecttest.com/connecttest.txt",
        "http://www.google.com"
    ];

    public int TimeoutSeconds { get; init; } = 5;
}
