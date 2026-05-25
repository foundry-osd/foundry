using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Discovers tenant group tags available to WinPE hardware hash upload.
/// </summary>
public interface IAutopilotGroupTagDiscoveryService
{
    Task<IReadOnlyList<string>> DiscoverAsync(
        DeployAutopilotHardwareHashUploadSettings settings,
        string workspaceRootPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses the media certificate to authenticate to Graph and read current Windows Autopilot group tags.
/// </summary>
public sealed class AutopilotGroupTagDiscoveryService(
    IMediaSecretKeyReader mediaSecretKeyReader,
    IAutopilotGraphTokenService tokenService,
    AutopilotGraphImportClient graphImportClient) : IAutopilotGroupTagDiscoveryService
{
    private readonly IMediaSecretKeyReader mediaSecretKeyReader = mediaSecretKeyReader;
    private readonly IAutopilotGraphTokenService tokenService = tokenService;
    private readonly AutopilotGraphImportClient graphImportClient = graphImportClient;

    public async Task<IReadOnlyList<string>> DiscoverAsync(
        DeployAutopilotHardwareHashUploadSettings settings,
        string workspaceRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        byte[] mediaKey = await mediaSecretKeyReader.ReadAsync(workspaceRootPath, cancellationToken)
            .ConfigureAwait(false);
        byte[]? pfxBytes = null;
        try
        {
            pfxBytes = DeployMediaSecretEnvelopeProtector.DecryptBytes(
                settings.CertificatePfxSecret!,
                mediaKey);
            string pfxPassword = DeployMediaSecretEnvelopeProtector.DecryptString(
                settings.CertificatePfxPasswordSecret!,
                mediaKey);

            using X509Certificate2 certificate = AutopilotCertificateCredential.Load(
                pfxBytes,
                pfxPassword,
                settings.ActiveCertificateThumbprint!);
            string accessToken = await tokenService.AcquireAccessTokenAsync(
                settings.TenantId!,
                settings.ClientId!,
                certificate,
                cancellationToken).ConfigureAwait(false);

            return await graphImportClient.ListGroupTagsAsync(accessToken, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mediaKey);
            if (pfxBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }
        }
    }

    private static void ValidateSettings(DeployAutopilotHardwareHashUploadSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TenantId))
        {
            throw new InvalidOperationException("Tenant ID is missing from the Autopilot upload configuration.");
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException("Client ID is missing from the Autopilot upload configuration.");
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveCertificateThumbprint))
        {
            throw new InvalidOperationException("Certificate thumbprint is missing from the Autopilot upload configuration.");
        }

        if (settings.CertificatePfxSecret is null || settings.CertificatePfxPasswordSecret is null)
        {
            throw new InvalidOperationException("Encrypted certificate material is missing from the Autopilot upload configuration.");
        }
    }
}
