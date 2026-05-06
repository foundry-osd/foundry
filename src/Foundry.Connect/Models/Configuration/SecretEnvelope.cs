namespace Foundry.Connect.Models.Configuration;

public sealed record SecretEnvelope
{
    public string Kind { get; init; } = string.Empty;

    public string Algorithm { get; init; } = string.Empty;

    public string KeyId { get; init; } = string.Empty;

    public string Nonce { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Ciphertext { get; init; } = string.Empty;
}
