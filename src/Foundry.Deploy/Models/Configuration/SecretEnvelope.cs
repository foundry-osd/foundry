namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Carries an encrypted media secret envelope produced during boot media generation.
/// </summary>
public sealed record SecretEnvelope
{
    /// <summary>
    /// Gets the envelope kind.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets the encryption algorithm identifier.
    /// </summary>
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>
    /// Gets the key identifier used to resolve the media secret key.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the base64 encoded encryption nonce.
    /// </summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>
    /// Gets the base64 encoded authentication tag.
    /// </summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>
    /// Gets the base64 encoded ciphertext.
    /// </summary>
    public string Ciphertext { get; init; } = string.Empty;
}
