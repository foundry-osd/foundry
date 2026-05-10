namespace Foundry.Models.Configuration;

public sealed record WifiSettings
{
    public bool IsEnabled { get; init; }
    public string? Ssid { get; init; }
    public string? SecurityType { get; init; }
    public string? Passphrase { get; init; }
    public bool HasEnterpriseProfile { get; init; }
    public string? EnterpriseProfileTemplatePath { get; init; }
    public NetworkAuthenticationMode EnterpriseAuthenticationMode { get; init; } = NetworkAuthenticationMode.UserOnly;
    public bool AllowRuntimeCredentials { get; init; }
    public bool RequiresCertificate { get; init; }
    public string? CertificatePath { get; init; }
}
