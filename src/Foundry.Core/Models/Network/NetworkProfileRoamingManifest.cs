// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Models.Network;

/// <summary>
/// Describes Foundry-managed network profile material captured in WinPE for Windows import.
/// </summary>
/// <remarks>
/// The manifest has its own schema version, independent of the configuration schemas. Version 2 adds the ordered
/// <see cref="WifiProfiles"/> list and still writes its first entry to <see cref="WifiProfile"/>, so a reader that
/// only knows version 1 keeps importing the preferred Wi-Fi profile.
/// </remarks>
public sealed record NetworkProfileRoamingManifest
{
    /// <summary>
    /// Gets the manifest schema version written by this build.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Gets the manifest schema version.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets the last manifest update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the preferred captured Wi-Fi profile metadata: the first entry of <see cref="WifiProfiles"/>, kept for
    /// schema 1 readers.
    /// </summary>
    public NetworkProfileRoamingProfile? WifiProfile { get; init; }

    /// <summary>
    /// Gets every captured Wi-Fi profile, most preferred first (schema 2). Empty in a schema 1 manifest.
    /// </summary>
    public IReadOnlyList<NetworkProfileRoamingProfile> WifiProfiles { get; init; } = [];

    /// <summary>
    /// Gets the captured wired 802.1X profile metadata.
    /// </summary>
    public NetworkProfileRoamingProfile? WiredDot1xProfile { get; init; }

    /// <summary>
    /// Gets captured certificate metadata.
    /// </summary>
    public IReadOnlyList<NetworkProfileRoamingCertificate> Certificates { get; init; } = [];

    /// <summary>
    /// Returns the captured Wi-Fi profiles in import order: <see cref="WifiProfiles"/> when it has entries, else the
    /// single schema 1 <see cref="WifiProfile"/>, else none.
    /// </summary>
    /// <returns>The ordered Wi-Fi profiles, most preferred first.</returns>
    public IReadOnlyList<NetworkProfileRoamingProfile> GetOrderedWifiProfiles()
    {
        if (WifiProfiles is { Count: > 0 })
        {
            return WifiProfiles;
        }

        return WifiProfile is null ? [] : [WifiProfile];
    }
}

/// <summary>
/// Describes one captured network profile file.
/// </summary>
public sealed record NetworkProfileRoamingProfile
{
    /// <summary>
    /// Gets the profile relative path inside the artifact root.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Foundry source that produced this profile.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Gets the expected pre-OOBE connectivity behavior.
    /// </summary>
    public string ConnectivityExpectation { get; init; } = string.Empty;
}

/// <summary>
/// Describes one captured certificate file.
/// </summary>
public sealed record NetworkProfileRoamingCertificate
{
    /// <summary>
    /// Gets the certificate relative path inside the artifact root.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the certificate kind.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Gets the target Windows certificate store name.
    /// </summary>
    public string StoreName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the encrypted password secret relative path for PFX/private-key imports.
    /// </summary>
    public string? PasswordSecretRelativePath { get; init; }
}

/// <summary>
/// Defines shared network profile roaming artifact names and path conventions.
/// </summary>
public static class NetworkProfileRoamingArtifacts
{
    /// <summary>
    /// Gets the default WinPE artifact root where Foundry.Connect writes captured network profile material.
    /// </summary>
    public const string DefaultArtifactRootPath = @"X:\Foundry\Runtime\NetworkProfileRoaming";

    /// <summary>
    /// Gets the manifest file name.
    /// </summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>
    /// Gets the file name of the first (preferred) captured Wi-Fi profile.
    /// </summary>
    public const string WifiProfileFileName = "wifi-profile.xml";

    /// <summary>
    /// Gets the captured wired 802.1X profile file name.
    /// </summary>
    public const string WiredDot1xProfileFileName = "wired-dot1x-profile.xml";

    /// <summary>
    /// Gets the captured certificate directory name.
    /// </summary>
    public const string CertificatesDirectoryName = "certificates";

    /// <summary>
    /// Gets the public certificate kind token.
    /// </summary>
    public const string PublicCertificateKind = "publicCertificate";

    /// <summary>
    /// Gets the PFX/private-key certificate kind token.
    /// </summary>
    public const string PfxPrivateKeyKind = "pfxPrivateKey";

    /// <summary>
    /// Gets the Windows root certificate store name.
    /// </summary>
    public const string RootStoreName = "Root";

    /// <summary>
    /// Gets the Windows personal certificate store name.
    /// </summary>
    public const string MyStoreName = "My";

    /// <summary>
    /// Gets the manual Wi-Fi source token.
    /// </summary>
    public const string ManualWifiSource = "manualWifi";

    /// <summary>
    /// Gets the provisioned Wi-Fi source token.
    /// </summary>
    public const string ProvisionedWifiSource = "provisionedWifi";

    /// <summary>
    /// Gets the provisioned wired 802.1X source token.
    /// </summary>
    public const string ProvisionedWiredDot1xSource = "provisionedWiredDot1x";

    /// <summary>
    /// Gets the expectation token for profiles expected to connect before OOBE.
    /// </summary>
    public const string PreOobeConnectableExpectation = "preOobeConnectable";

    /// <summary>
    /// Gets the expectation token for import-only profiles.
    /// </summary>
    public const string ImportOnlyExpectation = "importOnly";

    /// <summary>
    /// Gets the expectation token for profiles that depend on Windows machine credentials.
    /// </summary>
    public const string DependsOnMachineCredentialExpectation = "dependsOnMachineCredential";

    /// <summary>
    /// Returns the file name of a captured Wi-Fi profile by its position in the ordered list:
    /// <c>wifi-profile.xml</c> for the first, then <c>wifi-profile-2.xml</c>, <c>wifi-profile-3.xml</c>, and so on.
    /// </summary>
    /// <param name="index">Zero-based position in the ordered Wi-Fi profile list.</param>
    /// <returns>The artifact file name.</returns>
    public static string GetWifiProfileFileName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return index == 0
            ? WifiProfileFileName
            : string.Create(CultureInfo.InvariantCulture, $"wifi-profile-{index + 1}.xml");
    }

    /// <summary>
    /// Returns whether the certificate path points to a PFX/P12 package.
    /// </summary>
    public static bool IsPfxPath(string certificatePath)
    {
        string extension = Path.GetExtension(certificatePath);
        return string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".p12", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a stable artifact file name that preserves the source extension and avoids same-name collisions.
    /// </summary>
    public static string CreateStableCertificateFileName(string certificatePath)
    {
        string extension = Path.GetExtension(certificatePath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(certificatePath);
        string pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(certificatePath))))
            [..16]
            .ToLowerInvariant();
        return $"{fileNameWithoutExtension}-{pathHash}{extension}";
    }

    /// <summary>
    /// Normalizes an artifact relative path for manifest storage.
    /// </summary>
    public static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }

    /// <summary>
    /// Returns whether a captured certificate is owned by the specified source token.
    /// </summary>
    public static bool IsCertificateOwnedBySource(NetworkProfileRoamingCertificate certificate, string source)
    {
        string rootPrefix = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, RootStoreName, source)) + "\\";
        string myPrefix = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, MyStoreName, source)) + "\\";
        return certificate.RelativePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            certificate.RelativePath.StartsWith(myPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
