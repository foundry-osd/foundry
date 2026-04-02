namespace Foundry.Connect.Models.Configuration;

public sealed class InternetProbeOptions
{
    public IReadOnlyList<string> ProbeUris { get; init; } =
    [
        "http://www.msftconnecttest.com/connecttest.txt",
        "http://www.google.com"
    ];

    public int TimeoutSeconds { get; init; } = 5;
}
