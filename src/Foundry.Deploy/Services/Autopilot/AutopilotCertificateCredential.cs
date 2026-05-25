using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Loads the media PFX with an ephemeral private key and validates it against the configured app certificate thumbprint.
/// </summary>
public static class AutopilotCertificateCredential
{
    public static X509Certificate2 Load(byte[] pfxBytes, string password, string expectedThumbprint)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThumbprint);

        try
        {
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
            string actualThumbprint = NormalizeThumbprint(certificate.Thumbprint);
            if (!string.Equals(actualThumbprint, NormalizeThumbprint(expectedThumbprint), StringComparison.Ordinal))
            {
                certificate.Dispose();
                throw new InvalidOperationException("The embedded PFX certificate does not match the configured app certificate thumbprint.");
            }

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("The embedded PFX certificate does not contain private key material.");
            }

            return certificate;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("The embedded PFX certificate could not be loaded.", ex);
        }
    }

    private static string NormalizeThumbprint(string? thumbprint)
    {
        string? normalized = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.ToUpperInvariant();
    }
}
