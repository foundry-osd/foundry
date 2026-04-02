namespace Foundry.Connect.Models.Configuration;

public sealed record Dot1xSettings
{
    public bool IsEnabled { get; init; }
    public string? ProfileTemplatePath { get; init; }
    public NetworkAuthenticationMode AuthenticationMode { get; init; } = NetworkAuthenticationMode.MachineOnly;
    public bool AllowRuntimeCredentials { get; init; }
    public bool RequiresCertificate { get; init; }
    public string? CertificatePath { get; init; }
}
