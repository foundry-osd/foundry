// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.WinPe;
using Foundry.Utilities.IO;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Generates Foundry.Connect runtime configuration (schema <see cref="ConfigurationSchemaVersions.ConnectCurrent"/>) and stages
/// network assets for boot media.
/// </summary>
/// <remarks>
/// <para>
/// Network secrets are protected by the deployment key only: a personal Wi-Fi passphrase and a client certificate
/// password become <see cref="MediaSecretEnvelopeProtector.DeploymentKeyId"/> envelopes, and a client PFX and an enterprise
/// Wi-Fi template that contains <c>keyMaterial</c> (or that cannot be read to tell) are staged as <c>&lt;file name&gt;.encrypted</c>,
/// a <see cref="SecretEnvelope"/> JSON document of the file bytes. Trusted root certificates and wired profiles stay plain files;
/// staged files sharing a name get a numeric suffix. No media key is created.
/// </para>
/// <para>
/// A network secret without enabled Password protection is refused (<see cref="InvalidOperationException"/>), even though
/// Foundry OSD already blocks media creation. The <c>protection</c> block, with its key check, is written only when at least one
/// secret was encrypted. A disabled feature writes no path, no envelope and no asset (CFG-08). An encrypted asset must not be
/// empty and its envelope must fit <see cref="ConnectProtectedFileLimits.MaxEnvelopeFileBytes"/>, the size Connect reads.
/// </para>
/// </remarks>
public sealed class ConnectConfigurationGenerator : IConnectConfigurationGenerator
{
    private const string StagedAssetRootFolderName = "FoundryConnectAssets";
    private const string MediaConfigRoot = @"Foundry\Config";
    private const string WiredProfileFolder = @"Network\Wired\Profiles";
    private const string WifiProfileFolder = @"Network\Wifi\Profiles";
    private const string WiredCertificateFolder = @"Network\Certificates\Wired";
    private const string WifiCertificateFolder = @"Network\Certificates\Wifi";
    private const string EncryptedFileSuffix = ".encrypted";

    // Runtime fields schema 6 no longer writes. Connect still reads them from older media, where they hold the values the
    // runtime records default to, so they are removed only from the serialized output.
    private static readonly string[] OmittedDot1xProperties = ["allowRuntimeCredentials", "authenticationMode", "requiresCertificate"];
    private static readonly string[] OmittedWifiProperties = ["allowRuntimeCredentials", "enterpriseAuthenticationMode", "requiresCertificate"];
    private const string OmittedRootProperty = "internetProbe";

    /// <inheritdoc />
    public FoundryConnectConfigurationDocument Generate(FoundryConfigurationDocument document, MediaInputs inputs, string stagingDirectoryPath)
    {
        return CreateProvisioningBundle(document, inputs, stagingDirectoryPath).Configuration;
    }

    /// <inheritdoc />
    public FoundryConnectProvisioningBundle CreateProvisioningBundle(
        FoundryConfigurationDocument document,
        MediaInputs inputs,
        string stagingDirectoryPath,
        string? authoringVersion = null,
        DeploymentMediaProtectionMaterial? protection = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectoryPath);

        // Unprotected material also carries a key, but that key is written in plaintext to the media: it never protects a network secret.
        bool isMediaProtected = protection?.Settings.IsEnabled == true;
        NetworkConfigurationValidationResult validationResult = NetworkConfigurationValidator.Validate(document.Network, inputs, isMediaProtected);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Network configuration is invalid: {validationResult.Code}.");
        }

        string assetRootPath = Path.Combine(stagingDirectoryPath, StagedAssetRootFolderName);
        DirectoryOperations.Recreate(assetRootPath);

        var staging = new AssetStaging(assetRootPath, isMediaProtected ? protection!.DeploymentKey : null);
        NetworkSettings network = document.Network;
        NetworkSecrets secrets = inputs.Network;

        ConnectDot1xSettings dot1x = StageDot1x(network.Dot1x, inputs, secrets, staging);
        ConnectWifiSettings wifi = StageWifi(network.WifiProvisioned && network.Wifi.IsEnabled, network.Wifi, inputs, secrets, staging);

        // The generated document uses media-relative asset paths; source file paths stay outside the runtime JSON.
        FoundryConnectConfigurationDocument configuration = new()
        {
            AuthoringVersion = authoringVersion,
            Capabilities = new ConnectNetworkCapabilitiesSettings
            {
                WifiProvisioned = network.WifiProvisioned
            },
            Network = new ConnectNetworkSettings
            {
                ProfileRoaming = new ConnectNetworkProfileRoamingSettings
                {
                    WiredDot1x = new NetworkProfileRoamingTransportSettings
                    {
                        IsEnabled = network.RoamWiredDot1xProfileToWindows,
                        IncludePrivateKeyMaterial = network.RoamWiredDot1xPrivateKeyMaterialToWindows
                    },
                    Wifi = new NetworkProfileRoamingTransportSettings
                    {
                        IsEnabled = network.RoamWifiProfileToWindows,
                        IncludePrivateKeyMaterial = network.RoamWifiPrivateKeyMaterialToWindows
                    }
                }
            },
            Dot1x = dot1x,
            Wifi = wifi,
            Telemetry = document.Telemetry,
            Protection = staging.HasProtectedContent ? protection!.Settings : null
        };

        return new FoundryConnectProvisioningBundle
        {
            Configuration = configuration,
            ConfigurationJson = Serialize(configuration),
            AssetFiles = staging.AssetFiles
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The schema 6 output omits <c>allowRuntimeCredentials</c>, <c>authenticationMode</c>, <c>enterpriseAuthenticationMode</c>,
    /// <c>requiresCertificate</c> and <c>internetProbe</c>; Foundry.Connect reads their defaults, which equal the values earlier
    /// schemas always wrote.
    /// </remarks>
    public string Serialize(FoundryConnectConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        JsonObject root = JsonSerializer.SerializeToNode(document, ConfigurationJsonDefaults.SerializerOptions)!.AsObject();
        root.Remove(OmittedRootProperty);
        RemoveProperties(root["dot1x"], OmittedDot1xProperties);
        RemoveProperties(root["wifi"], OmittedWifiProperties);
        return root.ToJsonString(ConfigurationJsonDefaults.SerializerOptions);
    }

    /// <summary>
    /// Maps authoring wired 802.1X settings to the Connect runtime record with staged media-relative paths.
    /// </summary>
    /// <remarks>Plaintext passwords are never copied; the schema 5 <c>certificatePath</c> fields are not written.</remarks>
    /// <param name="authoring">The authoring settings.</param>
    /// <param name="profileTemplatePath">The media-relative staged profile path, or <see langword="null"/> when none is staged.</param>
    /// <param name="clientCertificate">The staged client certificate, or <see langword="null"/> when none is staged.</param>
    /// <param name="trustedRootCertificates">The media-relative staged trusted root paths, or <see langword="null"/> when none is staged.</param>
    internal static ConnectDot1xSettings ToRuntime(
        Dot1xSettings authoring,
        string? profileTemplatePath,
        ConnectClientCertificateSettings? clientCertificate,
        IReadOnlyList<string>? trustedRootCertificates)
    {
        ArgumentNullException.ThrowIfNull(authoring);
        return new ConnectDot1xSettings
        {
            IsEnabled = authoring.IsEnabled,
            ProfileTemplatePath = profileTemplatePath,
            ClientCertificate = clientCertificate,
            TrustedRootCertificates = trustedRootCertificates
        };
    }

    /// <summary>
    /// Maps authoring Wi-Fi settings to the Connect runtime record with staged media-relative paths.
    /// </summary>
    /// <remarks>
    /// The security type is normalized only when Wi-Fi is provisioned, and the enterprise profile path is kept only for an enterprise
    /// profile. Plaintext secrets are never copied; the schema 5 <c>certificatePath</c> fields are not written.
    /// </remarks>
    /// <param name="authoring">The authoring settings.</param>
    /// <param name="isEnabled">Whether Wi-Fi is both provisioned on the media and enabled.</param>
    /// <param name="enterpriseProfileTemplatePath">The media-relative staged enterprise profile path, when one is staged.</param>
    /// <param name="isEnterpriseProfileTemplateProtected">Whether the staged enterprise profile is a deployment-keyed envelope file.</param>
    /// <param name="clientCertificate">The staged client certificate, when one is staged.</param>
    /// <param name="trustedRootCertificates">The media-relative staged trusted root paths, when any is staged.</param>
    /// <param name="passphraseSecret">The deployment-keyed personal passphrase envelope, when one is required.</param>
    internal static ConnectWifiSettings ToRuntime(
        WifiSettings authoring,
        bool isEnabled,
        string? enterpriseProfileTemplatePath,
        bool isEnterpriseProfileTemplateProtected,
        ConnectClientCertificateSettings? clientCertificate,
        IReadOnlyList<string>? trustedRootCertificates,
        SecretEnvelope? passphraseSecret)
    {
        ArgumentNullException.ThrowIfNull(authoring);
        bool hasEnterpriseProfilePath = isEnabled && authoring.HasEnterpriseProfile && enterpriseProfileTemplatePath is not null;
        return new ConnectWifiSettings
        {
            IsEnabled = isEnabled,
            Ssid = authoring.Ssid,
            SecurityType = isEnabled ? NetworkConfigurationValidator.NormalizeWifiSecurityType(authoring, hasPassphrase: passphraseSecret is not null) : null,
            PassphraseSecret = passphraseSecret,
            HasEnterpriseProfile = authoring.HasEnterpriseProfile,
            EnterpriseProfileTemplatePath = hasEnterpriseProfilePath ? enterpriseProfileTemplatePath : null,
            IsEnterpriseProfileTemplateProtected = hasEnterpriseProfilePath && isEnterpriseProfileTemplateProtected,
            ClientCertificate = clientCertificate,
            TrustedRootCertificates = trustedRootCertificates
        };
    }

    private static ConnectDot1xSettings StageDot1x(Dot1xSettings dot1x, MediaInputs inputs, NetworkSecrets secrets, AssetStaging staging)
    {
        if (!dot1x.IsEnabled)
        {
            return ToRuntime(dot1x, null, null, null);
        }

        string profilePath = staging.Copy(inputs.ResolveRequired(dot1x.ProfileTemplate)!, WiredProfileFolder);
        ConnectClientCertificateSettings? clientCertificate = StageClientCertificate(
            inputs.ResolveRequired(dot1x.ClientCertificate),
            secrets.WiredCertificatePfxPassword,
            WiredCertificateFolder,
            NetworkConfigurationValidationCode.WiredMediaProtectionRequired,
            staging);
        return ToRuntime(dot1x, profilePath, clientCertificate, StageTrustedRoots(dot1x.TrustedRootCertificates, inputs, WiredCertificateFolder, staging));
    }

    private static ConnectWifiSettings StageWifi(bool isEnabled, WifiSettings wifi, MediaInputs inputs, NetworkSecrets secrets, AssetStaging staging)
    {
        if (!isEnabled)
        {
            return ToRuntime(wifi, false, null, false, null, null, null);
        }

        // Same classification as NetworkConfigurationValidator: only an enterprise profile uses a client certificate, and only a
        // personal profile uses a passphrase, so a value left over from another security type is never staged.
        bool isEnterprise = wifi.HasEnterpriseProfile || NetworkConfigurationValidator.IsEnterpriseSecurityType(wifi.SecurityType);
        bool isPersonal = !isEnterprise && !string.IsNullOrWhiteSpace(secrets.WifiPassphrase) &&
            string.Equals(
                NetworkConfigurationValidator.NormalizeWifiSecurityType(wifi, hasPassphrase: true),
                NetworkConfigurationValidator.WifiSecurityPersonal,
                StringComparison.Ordinal);

        string? templatePath = null;
        bool isTemplateProtected = false;
        if (wifi.HasEnterpriseProfile)
        {
            (templatePath, isTemplateProtected) = StageEnterpriseTemplate(inputs.ResolveRequired(wifi.EnterpriseProfileTemplate)!, staging);
        }

        ConnectClientCertificateSettings? clientCertificate = isEnterprise
            ? StageClientCertificate(
                inputs.ResolveRequired(wifi.ClientCertificate),
                secrets.WifiCertificatePfxPassword,
                WifiCertificateFolder,
                NetworkConfigurationValidationCode.WifiMediaProtectionRequired,
                staging)
            : null;
        SecretEnvelope? passphraseSecret = isPersonal
            ? staging.EncryptString(secrets.WifiPassphrase!, NetworkConfigurationValidationCode.WifiMediaProtectionRequired)
            : null;

        return ToRuntime(
            wifi,
            isEnabled: true,
            templatePath,
            isTemplateProtected,
            clientCertificate,
            StageTrustedRoots(wifi.TrustedRootCertificates, inputs, WifiCertificateFolder, staging),
            passphraseSecret);
    }

    private static ConnectClientCertificateSettings? StageClientCertificate(
        string? sourcePath,
        string? password,
        string folder,
        NetworkConfigurationValidationCode protectionRequiredCode,
        AssetStaging staging)
    {
        if (sourcePath is null)
        {
            return null;
        }

        // A PFX carries the private key: it is a secret even without a password. An empty password writes no envelope, because the
        // envelope format refuses an empty ciphertext; the runtime then opens the PFX with an empty password.
        string path = staging.CopyEncrypted(sourcePath, folder, protectionRequiredCode);
        return new ConnectClientCertificateSettings
        {
            Path = path,
            IsContentProtected = true,
            PasswordSecret = string.IsNullOrEmpty(password) ? null : staging.EncryptString(password, protectionRequiredCode)
        };
    }

    private static (string Path, bool IsProtected) StageEnterpriseTemplate(string sourcePath, AssetStaging staging)
    {
        if (!staging.CanEncrypt)
        {
            return (staging.Copy(sourcePath, WifiProfileFolder), false);
        }

        byte[] content = File.ReadAllBytes(GetExistingFullPath(sourcePath));
        try
        {
            // Fail closed: only a template read completely without key material stays plain; an unreadable one is encrypted too.
            using var stream = new MemoryStream(content, writable: false);
            return WlanProfileReader.DetectKeyMaterial(stream) == WlanProfileKeyMaterial.Absent
                ? (staging.Copy(sourcePath, WifiProfileFolder), false)
                : (staging.WriteEncrypted(content, Path.GetFileName(sourcePath), WifiProfileFolder, NetworkConfigurationValidationCode.WifiMediaProtectionRequired), true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private static string[]? StageTrustedRoots(
        IReadOnlyList<AuthoringFileReference> roots,
        MediaInputs inputs,
        string folder,
        AssetStaging staging)
    {
        return roots.Count == 0
            ? null
            : [.. roots.Select(root => staging.Copy(inputs.ResolveRequired(root)!, folder))];
    }

    private static void RemoveProperties(JsonNode? node, string[] propertyNames)
    {
        if (node is not JsonObject element)
        {
            return;
        }

        foreach (string propertyName in propertyNames)
        {
            element.Remove(propertyName);
        }
    }

    private static string GetExistingFullPath(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Foundry Connect asset source file was not found: '{fullSourcePath}'.", fullSourcePath);
        }

        return fullSourcePath;
    }

    private static string NormalizeEmbeddedRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }

    /// <summary>
    /// Stages the asset files of one generation and encrypts network secrets with the deployment key of protected media.
    /// </summary>
    private sealed class AssetStaging(string assetRootPath, byte[]? deploymentKey)
    {
        private readonly List<FoundryConnectProvisionedAssetFile> assetFiles = [];
        private readonly HashSet<string> stagedRelativePaths = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<FoundryConnectProvisionedAssetFile> AssetFiles => assetFiles;

        /// <summary>Gets whether a deployment key is available, that is whether Password protection is on.</summary>
        public bool CanEncrypt => deploymentKey is not null;

        /// <summary>Gets whether at least one envelope or encrypted asset was written.</summary>
        public bool HasProtectedContent { get; private set; }

        /// <summary>Copies a file as is and returns its media-relative path.</summary>
        public string Copy(string sourcePath, string folder)
        {
            string fullSourcePath = GetExistingFullPath(sourcePath);
            string stagedFilePath = PrepareStagedFile(Path.GetFileName(fullSourcePath), folder, out string relativePath);
            File.Copy(fullSourcePath, stagedFilePath, overwrite: true);
            return relativePath;
        }

        /// <summary>Stages a file as <c>&lt;file name&gt;.encrypted</c> and returns its media-relative path.</summary>
        public string CopyEncrypted(string sourcePath, string folder, NetworkConfigurationValidationCode protectionRequiredCode)
        {
            RequireKey(protectionRequiredCode);
            string fullSourcePath = GetExistingFullPath(sourcePath);
            byte[] content = File.ReadAllBytes(fullSourcePath);
            try
            {
                return WriteEncrypted(content, Path.GetFileName(fullSourcePath), folder, protectionRequiredCode);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }

        /// <summary>
        /// Writes <paramref name="content"/> as a deployment-keyed <see cref="SecretEnvelope"/> JSON file named
        /// <c>&lt;<paramref name="fileName"/>&gt;.encrypted</c>; the caller zeroes <paramref name="content"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="content"/> is empty, or its envelope would exceed <see cref="ConnectProtectedFileLimits.MaxEnvelopeFileBytes"/>,
        /// which Connect would refuse to read. The message names the staging folder only, never the file.
        /// </exception>
        public string WriteEncrypted(byte[] content, string fileName, string folder, NetworkConfigurationValidationCode protectionRequiredCode)
        {
            byte[] key = RequireKey(protectionRequiredCode);
            if (content.Length == 0)
            {
                throw new InvalidOperationException($"Foundry Connect protected asset in '{folder}' is empty and cannot be staged.");
            }

            SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptBytes(content, key, MediaSecretEnvelopeProtector.DeploymentKeyId);
            byte[] envelopeJson = JsonSerializer.SerializeToUtf8Bytes(envelope, ConfigurationJsonDefaults.SerializerOptions);
            if (envelopeJson.Length > ConnectProtectedFileLimits.MaxEnvelopeFileBytes)
            {
                throw new InvalidOperationException(
                    $"Foundry Connect protected asset in '{folder}' is too large: its encrypted file would exceed {ConnectProtectedFileLimits.MaxEnvelopeFileBytes} bytes.");
            }

            string stagedFilePath = PrepareStagedFile(fileName + EncryptedFileSuffix, folder, out string relativePath);
            File.WriteAllBytes(stagedFilePath, envelopeJson);
            HasProtectedContent = true;
            return relativePath;
        }

        /// <summary>Encrypts a secret string with the deployment key.</summary>
        public SecretEnvelope EncryptString(string value, NetworkConfigurationValidationCode protectionRequiredCode)
        {
            SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString(value, RequireKey(protectionRequiredCode), MediaSecretEnvelopeProtector.DeploymentKeyId);
            HasProtectedContent = true;
            return envelope;
        }

        // Validation already refuses a network secret without protection; this guard keeps every generation path fail-closed.
        private byte[] RequireKey(NetworkConfigurationValidationCode protectionRequiredCode) =>
            deploymentKey ?? throw new InvalidOperationException($"Network configuration is invalid: {protectionRequiredCode}.");

        // Two sources may share a file name (trusted roots are distinct by content, not by name). The first keeps its name and each
        // later one gets "-2", "-3", ... before its first dot, compared case-insensitively like the media file system, so every
        // staged path and media destination is distinct and deterministic for the same input order.
        private string PrepareStagedFile(string fileName, string folder, out string relativePath)
        {
            string stagedRelativePath = Path.Combine(folder, fileName);
            int dotIndex = fileName.IndexOf('.', StringComparison.Ordinal);
            for (int suffix = 2; !stagedRelativePaths.Add(stagedRelativePath); suffix++)
            {
                stagedRelativePath = Path.Combine(
                    folder,
                    dotIndex < 0
                        ? $"{fileName}-{suffix}"
                        : $"{fileName[..dotIndex]}-{suffix}{fileName[dotIndex..]}");
            }

            string stagedFilePath = Path.Combine(assetRootPath, stagedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!);
            assetFiles.Add(new FoundryConnectProvisionedAssetFile
            {
                SourcePath = stagedFilePath,
                RelativeDestinationPath = NormalizeEmbeddedRelativePath(Path.Combine(MediaConfigRoot, stagedRelativePath))
            });
            relativePath = NormalizeEmbeddedRelativePath(stagedRelativePath);
            return stagedFilePath;
        }
    }
}
