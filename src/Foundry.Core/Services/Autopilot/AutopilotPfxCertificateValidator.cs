// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Foundry.Core.Services.Autopilot;

/// <summary>
/// Validates operator-provided Autopilot PFX material before it is embedded into generated media.
/// </summary>
public static class AutopilotPfxCertificateValidator
{
    /// <summary>
    /// Validates that a password-protected PFX contains private key material and returns its certificate metadata.
    /// </summary>
    /// <param name="pfxBytes">PFX bytes provided by the operator.</param>
    /// <param name="password">PFX password provided by the operator.</param>
    /// <returns>Validation result describing whether the PFX can be embedded into generated media.</returns>
    public static AutopilotPfxValidationResult Validate(byte[] pfxBytes, string? password)
    {
        return Validate(pfxBytes, password, expectedThumbprint: null, requireExpectedThumbprint: false);
    }

    /// <summary>
    /// Validates that a password-protected PFX contains private key material for the configured certificate.
    /// </summary>
    /// <param name="pfxBytes">PFX bytes provided by the operator.</param>
    /// <param name="password">PFX password provided by the operator.</param>
    /// <param name="expectedThumbprint">Thumbprint of the certificate configured on the managed app registration.</param>
    /// <returns>Validation result describing whether the PFX can be embedded into generated media.</returns>
    public static AutopilotPfxValidationResult Validate(byte[] pfxBytes, string? password, string? expectedThumbprint)
    {
        return Validate(pfxBytes, password, expectedThumbprint, requireExpectedThumbprint: true);
    }

    private static AutopilotPfxValidationResult Validate(
        byte[] pfxBytes,
        string? password,
        string? expectedThumbprint,
        bool requireExpectedThumbprint)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);

        if (pfxBytes.Length == 0)
        {
            return AutopilotPfxValidationResult.Failure(AutopilotPfxValidationCode.PfxRequired);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return AutopilotPfxValidationResult.Failure(AutopilotPfxValidationCode.PasswordRequired);
        }

        string? normalizedExpectedThumbprint = NormalizeThumbprint(expectedThumbprint);
        if (requireExpectedThumbprint && string.IsNullOrWhiteSpace(normalizedExpectedThumbprint))
        {
            return AutopilotPfxValidationResult.Failure(AutopilotPfxValidationCode.ExpectedThumbprintRequired);
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
            string actualThumbprint = NormalizeThumbprint(certificate.Thumbprint)!;
            if (!string.IsNullOrWhiteSpace(normalizedExpectedThumbprint) &&
                !string.Equals(actualThumbprint, normalizedExpectedThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return AutopilotPfxValidationResult.Failure(AutopilotPfxValidationCode.ThumbprintMismatch, actualThumbprint);
            }

            if (!certificate.HasPrivateKey)
            {
                return AutopilotPfxValidationResult.Failure(AutopilotPfxValidationCode.PrivateKeyMissing, actualThumbprint);
            }

            return AutopilotPfxValidationResult.Success(actualThumbprint, certificate.NotAfter.ToUniversalTime());
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return AutopilotPfxValidationResult.Failure(AutopilotPfxValidationCode.InvalidPfx);
        }
    }

    private static string? NormalizeThumbprint(string? thumbprint)
    {
        string? normalized = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToUpperInvariant();
    }
}

public sealed record AutopilotPfxValidationResult
{
    /// <summary>
    /// Gets whether the PFX is valid for media embedding.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the validation outcome code.
    /// </summary>
    public AutopilotPfxValidationCode Code { get; init; }

    /// <summary>
    /// Gets the normalized certificate thumbprint when it could be read.
    /// </summary>
    public string? Thumbprint { get; init; }

    /// <summary>
    /// Gets the certificate expiration time when validation succeeds.
    /// </summary>
    public DateTimeOffset? ExpiresOnUtc { get; init; }

    /// <summary>
    /// Creates a successful PFX validation result.
    /// </summary>
    /// <param name="thumbprint">Normalized certificate thumbprint.</param>
    /// <param name="expiresOnUtc">Certificate expiration time.</param>
    /// <returns>Successful validation result.</returns>
    public static AutopilotPfxValidationResult Success(string thumbprint, DateTimeOffset expiresOnUtc)
    {
        return new AutopilotPfxValidationResult
        {
            IsValid = true,
            Code = AutopilotPfxValidationCode.Valid,
            Thumbprint = thumbprint,
            ExpiresOnUtc = expiresOnUtc
        };
    }

    /// <summary>
    /// Creates a failed PFX validation result.
    /// </summary>
    /// <param name="code">Failure code.</param>
    /// <param name="thumbprint">Normalized certificate thumbprint, when available.</param>
    /// <returns>Failed validation result.</returns>
    public static AutopilotPfxValidationResult Failure(
        AutopilotPfxValidationCode code,
        string? thumbprint = null)
    {
        return new AutopilotPfxValidationResult
        {
            IsValid = false,
            Code = code,
            Thumbprint = thumbprint
        };
    }
}

public enum AutopilotPfxValidationCode
{
    /// <summary>The PFX is valid for media embedding.</summary>
    Valid,

    /// <summary>No PFX bytes were provided.</summary>
    PfxRequired,

    /// <summary>No PFX password was provided.</summary>
    PasswordRequired,

    /// <summary>No expected active certificate thumbprint was configured.</summary>
    ExpectedThumbprintRequired,

    /// <summary>The PFX could not be loaded.</summary>
    InvalidPfx,

    /// <summary>The PFX leaf certificate thumbprint does not match the active app certificate.</summary>
    ThumbprintMismatch,

    /// <summary>The PFX does not contain private key material.</summary>
    PrivateKeyMissing
}
