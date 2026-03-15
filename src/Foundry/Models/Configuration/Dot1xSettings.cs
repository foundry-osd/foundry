namespace Foundry.Models.Configuration;

public sealed record Dot1xSettings
{
    public bool IsEnabled { get; init; }
    public string? CertificatePath { get; init; }
}
