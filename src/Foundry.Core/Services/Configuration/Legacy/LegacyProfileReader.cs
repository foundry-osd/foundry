// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>
/// Read-only reader for the retired deployment profile formats: exported packages, connection files and the local
/// profile store. Used by first-launch migration and import only; it never writes, recovers or locks legacy storage.
/// </summary>
/// <remarks>
/// The formats are frozen: every value this reader recomputes (secret identities, credential targets, associated data)
/// is a copy of what the retired feature wrote, independent of the current configuration services.
/// </remarks>
public static class LegacyProfileReader
{
    private const int MaximumDescriptorBytes = 64 * 1024;
    private const int MaximumRevisionBytes = 17 * 1024 * 1024;
    private const int PayloadFormatVersion = 1;

    // Legacy ProfileSecretPurpose values.
    private const int DeploymentPasswordPurpose = 0;
    private const int WifiPassphrasePurpose = 1;
    private const int AdministratorPasswordPurpose = 2;
    private const int AdditionalAccountPasswordPurpose = 3;
    private const int WiredCertificatePasswordPurpose = 4;
    private const int WifiCertificatePasswordPurpose = 5;
    private const int AutopilotCertificatePasswordPurpose = 6;
    private const int SharedProfileKeyPurpose = 7;
    private const int DomainJoinPasswordPurpose = 8;

    // Legacy ProfileValueState and ProfileAssetKind values.
    private const int BlankState = 3;
    private const int PresentState = 4;
    private const int WiredCertificateKind = 3;
    private const int WifiCertificateKind = 4;
    private const int AutopilotCertificateKind = 6;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Tolerant: unknown members, including the retired Domain Join members, are ignored.
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32
    };

    /// <summary>Returns true when <paramref name="bytes"/> starts with the legacy <c>FNDRYPRF</c> magic.</summary>
    public static bool IsLegacyPackage(ReadOnlySpan<byte> bytes) => LegacyPackageEnvelope.HasMagic(bytes);

    /// <summary>Decrypts an exported <c>.foundryprofile</c> or a legacy connection file.</summary>
    /// <exception cref="LegacyPasswordException">The passphrase does not open the package.</exception>
    /// <exception cref="InvalidDataException">The package is malformed, tampered or unsupported.</exception>
    public static LegacyConfigurationSnapshot ReadPackage(ReadOnlySpan<byte> package, ReadOnlySpan<char> passphrase)
    {
        byte[] plaintext = LegacyPackageEnvelope.DecryptWithPassphrase(package, passphrase);
        try
        {
            return ReadPayload(plaintext, descriptor: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Lists the committed profiles of a legacy store. A missing store is empty; a profile that cannot be read
    /// (malformed, inaccessible or redirected) is skipped so the others can still be migrated.
    /// </summary>
    /// <exception cref="IOException">The store root or its profiles directory is a redirected (reparse point) directory.</exception>
    public static IReadOnlyList<LegacyLocalProfile> ListLocalProfiles(string profilesRoot)
    {
        string root = NormalizeRoot(profilesRoot);
        string directory = Path.Combine(root, "profiles");
        if (!Directory.Exists(directory))
        {
            return [];
        }
        if (IsReparsePoint(root) || IsReparsePoint(directory))
        {
            throw new IOException("Legacy profile storage cannot use redirected files or directories.");
        }
        Guid? active = ReadActive(root);
        var profiles = new List<LegacyLocalProfile>();
        foreach (string path in Directory.GetDirectories(directory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(path), "N", out Guid localId) || localId == Guid.Empty)
            {
                continue;
            }
            try
            {
                if (ReadDescriptor(root, localId) is { } head)
                {
                    profiles.Add(new LegacyLocalProfile(localId, head.Descriptor.DisplayName!, localId == active, head.Descriptor.IsShared));
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // An unreadable profile cannot be migrated or imported; it is left untouched.
            }
        }
        return profiles.OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(profile => profile.LocalId).ToArray();
    }

    /// <summary>
    /// Decrypts the committed revision of a legacy local profile. A pending journal is ignored: the head always names
    /// a committed, readable revision.
    /// </summary>
    /// <exception cref="LegacyProfileLockedException">The revision key is not in Credential Manager for this user.</exception>
    /// <exception cref="FileNotFoundException">The profile has no committed revision.</exception>
    /// <exception cref="InvalidDataException">The descriptor or revision is malformed, tampered or unsupported.</exception>
    public static LegacyConfigurationSnapshot ReadLocalProfile(string profilesRoot, Guid localId, IWindowsCredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (localId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty legacy profile identity is required.", nameof(localId));
        }
        string root = NormalizeRoot(profilesRoot);
        (byte[] headBytes, LocalDescriptor descriptor) = ReadDescriptor(root, localId)
            ?? throw new FileNotFoundException("The legacy local profile does not exist.");
        using WindowsCredential key = credentials.Read(RevisionCredentialTarget(root, localId, descriptor.Revision))
            ?? throw new LegacyProfileLockedException(localId);
        byte[] revision = ReadBounded(root, ProfilePath(root, localId, "revisions", descriptor.Revision.ToString("N") + ".profile"), MaximumRevisionBytes);
        // The associated data is the descriptor exactly as committed on disk.
        byte[] plaintext = LegacyPackageEnvelope.DecryptLocalStorage(revision, key.Secret, headBytes);
        try
        {
            return ReadPayload(plaintext, descriptor);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static LegacyConfigurationSnapshot ReadPayload(byte[] plaintext, LocalDescriptor? descriptor)
    {
        Payload payload;
        FoundryConfigurationDocument configuration;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(plaintext, PayloadOptions) ?? throw new InvalidDataException("The legacy profile payload is missing.");
            if (payload.FormatVersion != PayloadFormatVersion || payload.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(payload.DisplayName)
                || payload.Configuration.ValueKind != JsonValueKind.Object || !payload.Configuration.TryGetProperty("schemaVersion", out JsonElement schema)
                || payload.Secrets?.Entries is not { } entries || entries.Any(entry => entry is null) || payload.Assets is null || payload.Assets.Any(asset => asset is null))
            {
                throw new InvalidDataException("The legacy profile payload is invalid or unsupported.");
            }
            int schemaVersion = schema.GetInt32();
            if (schemaVersion < 1 || schemaVersion > FoundryConfigurationDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"The legacy profile authoring schema is unsupported. SchemaVersion={schemaVersion}.");
            }
            JsonNode configurationNode = WithoutNullValues(JsonNode.Parse(payload.Configuration.GetRawText(), documentOptions: new() { MaxDepth = 32 })!);
            configuration = FoundryConfigurationMigration.ApplySchemaMigrations(
                configurationNode.Deserialize<FoundryConfigurationDocument>(PayloadOptions) ?? throw new InvalidDataException("The legacy profile configuration is missing."));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The legacy profile payload is invalid.", exception);
        }
        if (descriptor is not null && (payload.ProfileId != descriptor.ProfileId || !string.Equals(payload.DisplayName, descriptor.DisplayName, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The legacy local profile identity does not match its committed metadata.");
        }

        PayloadSecret[] secrets = payload.Secrets.Entries.OfType<PayloadSecret>().ToArray();
        PayloadAsset[] assets = payload.Assets.OfType<PayloadAsset>().ToArray();
        bool isConnectionFile = descriptor is null && secrets.Any(secret => secret.Purpose == SharedProfileKeyPurpose);
        return new LegacyConfigurationSnapshot(
            payload.DisplayName!,
            configuration,
            ReadSecrets(configuration, secrets, assets),
            ReadFiles(assets),
            WasShared: descriptor?.IsShared ?? isConnectionFile,
            IsConnectionFile: isConnectionFile);
    }

    /// <summary>
    /// Removes explicit null members and list items, so a null section reads as absent and takes its default instead of
    /// reaching migration and secret mapping as a null reference.
    /// </summary>
    private static JsonNode WithoutNullValues(JsonNode node)
    {
        if (node is JsonObject json)
        {
            foreach (string name in json.Where(member => member.Value is null).Select(member => member.Key).ToArray())
            {
                json.Remove(name);
            }
            foreach (KeyValuePair<string, JsonNode?> member in json)
            {
                WithoutNullValues(member.Value!);
            }
        }
        else if (node is JsonArray array)
        {
            for (int index = array.Count - 1; index >= 0; index--)
            {
                if (array[index] is { } item) WithoutNullValues(item);
                else array.RemoveAt(index);
            }
        }
        return node;
    }

    /// <summary>Maps each stored secret back to its kind by recomputing the legacy identity of every candidate setting.</summary>
    private static LegacySecret[] ReadSecrets(FoundryConfigurationDocument configuration, PayloadSecret[] secrets, PayloadAsset[] assets)
    {
        var candidates = new Dictionary<string, (LegacySecretKind Kind, string? Key)>(StringComparer.Ordinal);
        WifiSettings wifi = configuration.Network.Wifi;
        AutopilotTenantRegistrationSettings tenant = configuration.Autopilot.HardwareHashUpload.Tenant;
        Add(DeploymentPasswordPurpose, ["media-protection"], LegacySecretKind.DeploymentProtection);
        Add(WifiPassphrasePurpose, [wifi.Ssid ?? string.Empty, wifi.SecurityType ?? string.Empty], LegacySecretKind.WifiPassphrase);
        Add(AdministratorPasswordPurpose, ["builtin-administrator"], LegacySecretKind.OobeAdministrator);
        foreach (OobeAdditionalAccountSettings account in configuration.Customization.Oobe.AdditionalAccounts)
        {
            Add(AdditionalAccountPasswordPurpose, [account.Id, account.UserName ?? string.Empty, account.Type.ToString()], LegacySecretKind.OobeAccount, account.Id);
        }
        Add(WiredCertificatePasswordPurpose, [AssetHash(WiredCertificateKind)], LegacySecretKind.WiredCertificatePassword);
        Add(WifiCertificatePasswordPurpose, [AssetHash(WifiCertificateKind)], LegacySecretKind.WifiCertificatePassword);
        Add(AutopilotCertificatePasswordPurpose, [tenant.TenantId ?? string.Empty, tenant.ClientId ?? string.Empty, AssetHash(AutopilotCertificateKind)],
            LegacySecretKind.AutopilotCertificatePassword);
        DomainJoinSettings domainJoin = configuration.DomainJoin;
        foreach (string? account in domainJoin.Domains.Select(domain => domain.AccountName).Prepend(domainJoin.SharedAccountName))
        {
            if (string.IsNullOrWhiteSpace(account)) continue;
            string canonical = account.Trim().ToUpperInvariant();
            Add(DomainJoinPasswordPurpose, [canonical], LegacySecretKind.DomainJoin, canonical);
        }

        var result = new List<LegacySecret>();
        foreach (PayloadSecret secret in secrets.Where(secret => secret.State is PresentState or BlankState))
        {
            if (secret.Identity is null || !candidates.TryGetValue(CandidateKey(secret.Purpose, secret.Identity), out var match))
            {
                continue;
            }
            result.Add(new LegacySecret(match.Kind, match.Key, secret.State == BlankState ? string.Empty : DecodeSecret(secret.Value)));
        }
        return result.ToArray();

        void Add(int purpose, string[] context, LegacySecretKind kind, string? key = null) =>
            candidates.TryAdd(CandidateKey(purpose, Identity(purpose, context)), (kind, key));

        string AssetHash(int kind) => assets.Where(asset => asset.Kind == kind).ToArray() switch
        {
            [] => "unavailable",
            [var asset] => asset.Sha256 ?? "unavailable",
            _ => throw new InvalidDataException("A legacy profile dependency has multiple asset records.")
        };
    }

    /// <summary>Frozen copy of the legacy identity hash: SHA-256 over the default JSON of the purpose number and its context.</summary>
    private static string Identity(int purpose, string[] context) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { purpose, context }))));

    private static string CandidateKey(int purpose, string identity) => $"{purpose}:{identity}";

    private static string DecodeSecret(byte[]? value)
    {
        try
        {
            return value is { Length: > 0 } ? StrictUtf8.GetString(value) : throw new InvalidDataException("A legacy profile secret has no value.");
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("A legacy profile secret is not valid UTF-8.", exception);
        }
    }

    private static LegacyFile[] ReadFiles(PayloadAsset[] assets)
    {
        var files = new List<LegacyFile>();
        foreach (PayloadAsset asset in assets.Where(asset => asset.State == PresentState))
        {
            LegacyFileRole? role = asset.Kind switch
            {
                0 => LegacyFileRole.Unattend,
                1 => LegacyFileRole.WiredProfile,
                2 => LegacyFileRole.WifiProfile,
                WiredCertificateKind => LegacyFileRole.WiredCertificate,
                WifiCertificateKind => LegacyFileRole.WifiCertificate,
                AutopilotCertificateKind => LegacyFileRole.AutopilotCertificate,
                _ => null
            };
            if (role is not { } fileRole)
            {
                continue;
            }
            if (!IsIdentity(asset.Id) || asset.Content is null || asset.Sha256 is null
                || !string.Equals(Convert.ToHexString(SHA256.HashData(asset.Content)), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A legacy profile file failed integrity validation.");
            }
            files.Add(new LegacyFile(fileRole, fileRole == LegacyFileRole.Unattend ? asset.Id : null, asset.Id + SafeExtension(asset.RelativePath), asset.Content));
        }
        return files.ToArray();
    }

    private static bool IsIdentity(string? identity) => identity is { Length: > 0 and <= 128 } and not "." and not ".."
        && identity.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string SafeExtension(string? path) => Path.GetExtension(path)?.ToLowerInvariant() switch
    {
        ".xml" => ".xml",
        ".pfx" => ".pfx",
        ".p12" => ".p12",
        ".cer" => ".cer",
        ".crt" => ".crt",
        ".json" => ".json",
        _ => ".bin"
    };

    private static (byte[] Bytes, LocalDescriptor Descriptor)? ReadDescriptor(string root, Guid localId)
    {
        string path = ProfilePath(root, localId, "head.json");
        if (!File.Exists(path))
        {
            return null;
        }
        byte[] bytes = ReadBounded(root, path, MaximumDescriptorBytes);
        LocalDescriptor descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize<LocalDescriptor>(bytes, PayloadOptions) ?? throw new InvalidDataException("The legacy local profile metadata is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The legacy local profile metadata is invalid.", exception);
        }
        if (descriptor.Version != 1 || descriptor.LocalId != localId || descriptor.ProfileId == Guid.Empty || descriptor.Revision == Guid.Empty
            || string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            throw new InvalidDataException("The legacy local profile metadata is invalid.");
        }
        return (bytes, descriptor);
    }

    private static Guid? ReadActive(string root)
    {
        string path = Path.Combine(root, "active.json");
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            ActivePointer pointer = JsonSerializer.Deserialize<ActivePointer>(ReadBounded(root, path, MaximumDescriptorBytes), PayloadOptions)
                ?? throw new InvalidDataException("The legacy active profile pointer is missing.");
            return pointer.Version == 1 ? pointer.LocalId : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // The active selection only orders migration; an unreadable pointer selects no profile.
            return null;
        }
    }

    /// <summary>Frozen copy of the legacy target: the scope is the first 24 hex characters of SHA-256 over the upper-case root.</summary>
    private static string RevisionCredentialTarget(string root, Guid localId, Guid revision)
    {
        string scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..24];
        return $"FoundryOSD/Profiles/{scope}/{localId:N}/Revision/{revision:N}";
    }

    private static string NormalizeRoot(string profilesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRoot);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(profilesRoot));
    }

    private static string ProfilePath(string root, Guid localId, params string[] segments) =>
        Path.Combine([root, "profiles", localId.ToString("N"), .. segments]);

    private static bool IsReparsePoint(string path) =>
        (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>Reads a bounded file, refusing redirected files or directories between it and the store root.</summary>
    private static byte[] ReadBounded(string root, string path, int maximumLength)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (IsReparsePoint(current))
            {
                throw new IOException("Legacy profile storage cannot use redirected files or directories.");
            }
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximumLength)
        {
            throw new InvalidDataException("The legacy profile file size is invalid.");
        }
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed record Payload(int FormatVersion, Guid ProfileId, string? DisplayName, JsonElement Configuration, PayloadSecrets? Secrets, IReadOnlyList<PayloadAsset?>? Assets);

    private sealed record PayloadSecrets(IReadOnlyList<PayloadSecret?>? Entries);

    private sealed record PayloadSecret(int Purpose, string? Identity, int State, byte[]? Value);

    private sealed record PayloadAsset(string? Id, int Kind, string? RelativePath, int State, string? Sha256, byte[]? Content);

    private sealed record LocalDescriptor(int Version, Guid LocalId, Guid ProfileId, Guid Revision, string? DisplayName, JsonElement Enrollment)
    {
        public bool IsShared => Enrollment.ValueKind == JsonValueKind.Object;
    }

    private sealed record ActivePointer(int Version, Guid? LocalId);
}
